const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const scriptNames = [
    'chart.webgpu.shaders.js',
    'chart.webgpu.lifecycle.js',
    'chart.webgpu.data.js',
    'chart.webgpu.pyramid.js',
    'chart.webgpu.js',
];
const scripts = scriptNames.map(name => ({
    name,
    source: fs.readFileSync(path.join(__dirname, '../../src/Nexus.UI/src/charts', name), 'utf8'),
}));

function deferred() {
    let resolve;
    const promise = new Promise(value => resolve = value);
    return { promise, resolve };
}

function createBuffer(size, options = {}) {
    return {
        size,
        destroyed: false,
        mapState: 'unmapped',
        async mapAsync() {
            if (options.mapAsync)
                await options.mapAsync(this);
            this.mapState = 'mapped';
        },
        getMappedRange() { return new ArrayBuffer(size); },
        unmap() { this.mapState = 'unmapped'; },
        destroy() { this.destroyed = true; },
    };
}

function createDevice(options = {}) {
    const lost = deferred();
    const device = {
        limits: {
            maxBufferSize: options.maxBufferSize ?? 1024 * 1024 * 1024,
            maxStorageBufferBindingSize: options.maxStorageBufferBindingSize ?? 1024 * 1024 * 1024,
        },
        lost: lost.promise,
        destroyed: false,
        buffers: [],
        commands: [],
        submissions: 0,
        createShaderModule() { return {}; },
        createRenderPipeline() { return { getBindGroupLayout() { return {}; } }; },
        createComputePipeline({ compute }) { return { entryPoint: compute.entryPoint, getBindGroupLayout() { return {}; } }; },
        createBuffer({ size }) {
            const buffer = createBuffer(size, options);
            this.buffers.push(buffer);
            return buffer;
        },
        createBindGroup({ entries }) { return { entries }; },
        createCommandEncoder() {
            device.commands.push({ type: 'encoder' });
            return {
                beginComputePass() {
                    let pipeline, bindGroup;
                    device.commands.push({ type: 'compute' });
                    return {
                        setPipeline(value) { pipeline = value; },
                        setBindGroup(_index, value) { bindGroup = value; },
                        dispatchWorkgroups(count) { device.commands.push({ type: 'dispatch', pipeline, bindGroup, count }); },
                        end() {},
                    };
                },
                beginRenderPass() {
                    let pipeline, bindGroup;
                    device.commands.push({ type: 'render' });
                    return {
                        setPipeline(value) { pipeline = value; },
                        setScissorRect() {},
                        setBindGroup(_index, value) { bindGroup = value; },
                        draw(count) { device.commands.push({ type: 'draw', pipeline, bindGroup, count }); },
                        end() {},
                    };
                },
                copyBufferToBuffer(source, sourceOffset, destination, destinationOffset, size) {
                    assert.ok(sourceOffset + size <= source.size);
                    assert.ok(destinationOffset + size <= destination.size);
                    device.commands.push({ type: 'copy', source, sourceOffset, destination, destinationOffset, size });
                },
                finish() { return {}; },
            };
        },
        queue: {
            writeBuffer(buffer, offset, data) {
                const bytes = ArrayBuffer.isView(data)
                    ? new Uint8Array(data.buffer, data.byteOffset, data.byteLength)
                    : new Uint8Array(data);
                assert.ok(offset + bytes.byteLength <= buffer.size);
                device.commands.push({ type: 'write', buffer, offset, data: bytes.slice().buffer });
            },
            submit() { device.submissions++; device.commands.push({ type: 'submit' }); },
            async onSubmittedWorkDone() {},
        },
        destroy() { this.destroyed = true; },
        lose(info = { message: 'test loss' }) { lost.resolve(info); },
    };
    return device;
}

function createEnvironment(options = {}) {
    const devices = [];
    const mapRequests = [];
    let requestDeviceCalls = 0;
    const failures = [];
    const canvases = new Map();
    const hooks = {};
    const adapter = {
        limits: {
            maxBufferSize: 1024 * 1024 * 1024,
            maxStorageBufferBindingSize: 1024 * 1024 * 1024,
        },
        async requestDevice() {
            requestDeviceCalls++;
            if (options.failFirstDevice && requestDeviceCalls === 1)
                throw new Error('device creation failed');

            const device = createDevice({
                maxBufferSize: options.maxBufferSize,
                maxStorageBufferBindingSize: options.maxStorageBufferBindingSize,
                mapAsync: options.deferMapAsync
                    ? () => new Promise(resolve => mapRequests.push(resolve))
                    : null,
            });
            devices.push(device);
            if (options.loseDeviceOnCreation)
                device.lose();
            return device;
        },
    };
    const context = {
        devicePixelRatio: options.dpr ?? 1,
        console: { error() {}, log() {}, warn() {} },
        Promise,
        Map,
        Set,
        ArrayBuffer,
        Uint8Array,
        Uint32Array,
        Float32Array,
        DataView,
        Error,
        Number,
        Math,
        JSON,
        performance: { now: () => 1 },
        GPUBufferUsage: { STORAGE: 1, COPY_DST: 2, COPY_SRC: 4, MAP_READ: 8, UNIFORM: 16 },
        GPUMapMode: { READ: 1 },
        navigator: options.noGpu ? {} : {
            gpu: {
                async requestAdapter() { return options.noAdapter ? null : adapter; },
                getPreferredCanvasFormat() { return 'rgba8unorm'; },
            },
        },
        document: {
            getElementById(id) {
                if (!canvases.has(id)) {
                    canvases.set(id, options.nullCanvasContext
                        ? { getContext() { return null; } }
                        : {
                            clientWidth: options.width ?? 100,
                            clientHeight: 100,
                            getContext() {
                                return {
                                    configure() {},
                                    unconfigure() {},
                                    getCurrentTexture() { return { createView() { return {}; } }; },
                                };
                            },
                        });
                }
                return canvases.get(id);
            },
        },
        __nexusChartWebGpuTestHooks: hooks,
    };
    context.window = context;
    context.globalThis = context;
    for (const script of scripts)
        vm.runInNewContext(script.source, context, { filename: script.name });

    function helper(chartId) {
        return {
            invokeMethodAsync(method, title, message) {
                failures.push({ chartId, method, title, message });
                return Promise.resolve();
            },
        };
    }

    return {
        api: context.nexus.chartWebGpu,
        hooks,
        devices,
        mapRequests,
        failures,
        helper,
        get requestDeviceCalls() { return requestDeviceCalls; },
    };
}

async function settle() {
    await new Promise(resolve => setImmediate(resolve));
    await new Promise(resolve => setImmediate(resolve));
}

test('charts share one WebGPU device and pipelines', async () => {
    const environment = createEnvironment();

    environment.api.initialize('first', environment.helper('first'));
    environment.api.initialize('second', environment.helper('second'));
    await settle();

    assert.equal(environment.requestDeviceCalls, 1);
    assert.equal(environment.hooks.instances.size, 2);
    assert.equal(
        environment.hooks.instances.get('first').device,
        environment.hooks.instances.get('second').device);

    environment.api.dispose('first');
    assert.equal(environment.devices[0].destroyed, false);
    environment.api.dispose('second');
    assert.equal(environment.devices[0].destroyed, true);

    environment.api.initialize('third', environment.helper('third'));
    await settle();
    assert.equal(environment.requestDeviceCalls, 2);
    assert.equal(environment.devices[1].destroyed, false);
    environment.api.dispose('third');
});

test('remote points use the existing drawing pipeline without reduction or raw requests', async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();
    const instance = environment.hooks.instances.get('chart');
    const plot = { plotLeft: 10, plotWidth: 100 };
    const points = new Float32Array([0, 1, 0.2, NaN, 1, 3]);
    const series = { id: 'remote', remotePoints: points };
    const item = environment.hooks.getRemoteRenderItem(instance, series, 'series', plot);
    assert.equal(item.seriesBuffer.dataMode, 1);
    assert.equal(item.zoomInfo.segmentCount, 2);
    assert.equal(item.zoomInfo.dx, 100);
    assert.equal(item.zoomInfo.zoomedLeft, 10);
    assert.equal(instance.rawRequests.size, 0);
    assert.equal(item.seriesBuffer.decimations, undefined);
    assert.equal(environment.hooks.getRemoteRenderItem(instance, series, 'series', plot).seriesBuffer, item.seriesBuffer);

    const commandStart = instance.device.commands.length;
    await environment.hooks.scheduleRender('chart', {
        plot: { left: 0, top: 0, right: 1, bottom: 1 },
        zoom: { left: 0.2, right: 0.3 },
        series: [{ ...series, axisMin: 0, axisMax: 10, length: 1e12, sampleStep: 1e-12 }],
    });
    const commands = instance.device.commands.slice(commandStart);
    assert.equal(commands.filter(command => command.type === 'compute').length, 0);
    assert.equal(instance.rawRequests.size, 0);
    const draws = commands.filter(command => command.type === 'draw');
    assert.deepEqual(draws.map(draw => draw.count), [12, 36]);
    for (const draw of draws) {
        assert.equal(draw.pipeline, instance.pipeline);
        assert.equal(draw.bindGroup.entries.find(entry => entry.binding === 2).resource.buffer, item.seriesBuffer.buffer);
        const uniform = draw.bindGroup.entries.find(entry => entry.binding === 0).resource.buffer;
        const data = commands.find(command => command.type === 'write' && command.buffer === uniform).data;
        assert.equal(new Uint32Array(data)[22], 1);
        assert.deepEqual([...new Float32Array(data).slice(10, 12)], [0, 100]);
    }

    const next = environment.hooks.getRemoteRenderItem(instance, { ...series, remotePoints: new Float32Array([0, 4, 1, 5]) }, 'series', plot);
    assert.equal(item.seriesBuffer.buffer.destroyed, true);
    assert.equal(next.seriesBuffer.buffer.destroyed, false);
    assert.throws(() => environment.hooks.getRemoteRenderItem(instance, { ...series, remotePoints: new Float32Array([NaN, 1]) }, 'series', plot), /coordinates/);
    environment.api.releaseTarget('chart', 'series');
    assert.equal(next.seriesBuffer.buffer.destroyed, true);
    environment.api.dispose('chart');
});

test('device loss fails every chart using the shared device', async () => {
    const environment = createEnvironment();
    environment.api.initialize('first', environment.helper('first'));
    environment.api.initialize('second', environment.helper('second'));
    await settle();

    environment.devices[0].lose();
    await settle();

    assert.equal(environment.hooks.instances.size, 0);
    assert.deepEqual(
        environment.failures.map(failure => [failure.chartId, failure.title]).sort(),
        [['first', 'GPU connection lost'], ['second', 'GPU connection lost']]);
});

test('device loss before instance publication does not publish a stale instance', async () => {
    const environment = createEnvironment({ loseDeviceOnCreation: true });
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();

    assert.equal(environment.hooks.instances.has('chart'), false);
    assert.equal(environment.failures.length, 1);
    assert.equal(environment.failures[0].title, 'WebGPU initialization failed');
    assert.match(environment.failures[0].message, /lost during chart initialization/i);
});

test('failed initialization can be retried', async () => {
    const environment = createEnvironment({ failFirstDevice: true });
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();

    assert.equal(environment.failures.length, 1);
    assert.equal(environment.failures[0].title, 'WebGPU initialization failed');
    assert.equal(environment.hooks.pendingInstances.size, 0);

    assert.equal(await environment.api.retry('chart'), true);
    assert.equal(environment.requestDeviceCalls, 2);
    assert.equal(environment.hooks.instances.has('chart'), true);
});

test('unsupported WebGPU reports one actionable failure', async () => {
    const environment = createEnvironment({ noGpu: true });
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();

    assert.equal(environment.failures.length, 1);
    assert.equal(environment.failures[0].title, 'WebGPU unavailable');
    assert.match(environment.failures[0].message, /hardware acceleration/i);
});

test('invalid chunked uploads destroy partial buffers and fail the chart', async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();

    const token = await environment.api.beginChunkedSeries('chart', 'series', 1, 4);
    const upload = environment.hooks.instances.get('chart').chunkedUploadSessions.get(token);
    assert.throws(
        () => environment.api.appendChunkedSeries('chart', token, 1, new Float32Array([0])),
        /expected sample offset 0/);
    await settle();

    assert.equal(upload.transientBuffer.destroyed, true);
    assert.equal(upload.overviewBuffer.destroyed, true);
    assert.equal(environment.hooks.instances.has('chart'), false);
    assert.equal(environment.failures[0].title, 'WebGPU upload failed');
});

test('terminal upload failures invalidate the instance and notify the chart', async () => {
    const environment = createEnvironment({ maxStorageBufferBindingSize: 8 });
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();

    await assert.rejects(environment.api.beginChunkedSeries('chart', 'series', 1, 3), /GPU storage buffer limit of 8 bytes/);
    await settle();

    assert.equal(environment.hooks.instances.has('chart'), false);
    assert.equal(environment.failures.length, 1);
    assert.equal(environment.failures[0].title, 'WebGPU upload failed');
});

test('missing WebGPU canvas context reports a terminal rendering failure', async () => {
    const environment = createEnvironment({ nullCanvasContext: true });
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();

    environment.api.renderSeries('chart', {});
    await settle();

    assert.equal(environment.hooks.instances.has('chart'), false);
    assert.equal(environment.failures.length, 1);
    assert.equal(environment.failures[0].title, 'WebGPU rendering failed');
    assert.match(environment.failures[0].message, /canvas context/i);
});

test('raw-detail cache evicts least recently used chunks to its budget', async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();
    const instance = environment.hooks.instances.get('chart');
    instance.cacheBudget = 8;
    const first = { buffer: createBuffer(4), pointBuffer: null, byteLength: 4, lastUsed: 1, decimations: new Map() };
    first.buffer.__nexusByteLength = 4;
    first.buffer.__nexusDestroyed = false;
    first.pointBuffer = first.buffer;
    const second = { buffer: createBuffer(4), pointBuffer: null, byteLength: 4, lastUsed: 2, decimations: new Map() };
    second.buffer.__nexusByteLength = 4;
    second.buffer.__nexusDestroyed = false;
    second.pointBuffer = second.buffer;
    instance.rawChunks.set('first', first);
    instance.rawChunks.set('second', second);
    instance.ownedGpuBytes = 8;

    environment.hooks.evictRawChunks(instance, 4);

    assert.equal(first.buffer.destroyed, true);
    assert.equal(instance.rawChunks.has('first'), false);
    assert.equal(instance.rawChunks.has('second'), true);
    assert.equal(instance.ownedGpuBytes, 4);
});

test('raw chunks selected earlier in a frame remain pinned under later cache pressure', async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();
    const instance = environment.hooks.instances.get('chart');
    const firstBuffer = createBuffer(12);
    firstBuffer.__nexusByteLength = 12;
    firstBuffer.__nexusDestroyed = false;
    const firstChunk = {
        id: 'first', buffer: firstBuffer, pointBuffer: firstBuffer, byteLength: 12,
        offset: 0, length: 3, lastUsed: 1, dataMode: 0, decimations: new Map(),
    };
    instance.rawChunks.set('first:1:0', firstChunk);
    instance.ownedGpuBytes = 12;
    instance.cacheBudget = 12;
    instance.uploadGenerations.set('first', 1);
    instance.uploadGenerations.set('second', 1);
    const protectedKeys = new Set();
    const payload = { Zoom: { Left: 0, Right: 1 } };
    const plot = { plotLeft: 0, plotWidth: 100, plotTop: 0, plotBottom: 100, plotHeight: 100 };

    const firstItems = environment.hooks.getRawRenderItems(
        instance,
        { id: 'first', version: 1, length: 3, chartId: 'chart', generation: 1 },
        { SampleStep: 1 / 3 }, payload, plot, {}, 'series', protectedKeys);
    const secondItems = environment.hooks.getRawRenderItems(
        instance,
        { id: 'second', version: 1, length: 3, chartId: 'chart', generation: 1, kind: 'Sine' },
        { SampleStep: 1 / 3 }, payload, plot, {}, 'series', protectedKeys);

    assert.equal(firstItems.length, 1);
    assert.equal(secondItems, null);
    assert.equal(firstBuffer.destroyed, false);
    assert.equal(instance.rawChunks.has('first:1:0'), true);
});

test('an exact raw chunk boundary renders from the preceding overlap chunk', async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();
    const instance = environment.hooks.instances.get('chart');
    const length = environment.hooks.rawChunkLength * 2;
    const buffer = environment.hooks.createTrackedBuffer(instance, {
        size: (environment.hooks.rawChunkLength + 1) * Float32Array.BYTES_PER_ELEMENT,
        usage: 1,
    });
    const chunk = {
        id: 'series', buffer, pointBuffer: buffer, byteLength: buffer.size,
        offset: 0, length: environment.hooks.rawChunkLength + 1,
        lastUsed: 1, dataMode: 0, decimations: new Map(),
    };
    instance.rawChunks.set('series:1:0', chunk);
    instance.uploadGenerations.set('series', 1);
    const encoder = instance.device.createCommandEncoder();

    const items = environment.hooks.getRawRenderItems(
        instance,
        { id: 'series', version: 1, length, chartId: 'chart', generation: 1, kind: 'Sine' },
        { SampleStep: 1 / length },
        { Zoom: { Left: 0, Right: 0.5 } },
        { plotLeft: 0, plotWidth: 100, plotTop: 0, plotBottom: 100, plotHeight: 100 },
        encoder,
        'series',
        new Set());

    assert.equal(items.length, 1);
    assert.equal(instance.rawRequests.has('series:1:1'), true);
    environment.api.dispose('chart');
});

test('draw resources are trimmed when fewer series are rendered', async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();
    const instance = environment.hooks.instances.get('chart');
    const resources = Array.from({ length: 4 }, () => ({ uniformBuffer: createBuffer(96) }));
    instance.targetResources.set('series', resources);

    environment.hooks.trimDrawResources(instance, 'series', 2);

    assert.equal(resources.length, 2);
    assert.equal(instance.targetResources.get('series'), resources);

    environment.hooks.trimDrawResources(instance, 'series', 0);
    assert.equal(instance.targetResources.has('series'), false);
});

test('series color preserves its configured alpha', () => {
    const environment = createEnvironment();

    assert.deepEqual(
        Array.from(environment.hooks.colorOf({ Color: { Red: 255, Green: 128, Blue: 0, Alpha: 64 } })),
        [1, 128 / 255, 0, 64 / 255]);
});

test('tracked buffers enforce the total chart budget and release exactly once', async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();
    const instance = environment.hooks.instances.get('chart');
    instance.cacheBudget = 12;

    const first = environment.hooks.createTrackedBuffer(instance, { size: 8, usage: 1 });
    assert.equal(instance.ownedGpuBytes, 8);
    assert.throws(
        () => environment.hooks.createTrackedBuffer(instance, { size: 8, usage: 1 }),
        /GPU memory budget/);
    assert.equal(instance.ownedGpuBytes, 8);

    environment.hooks.destroyTrackedBuffer(instance, first);
    environment.hooks.destroyTrackedBuffer(instance, first);
    assert.equal(instance.ownedGpuBytes, 0);
});

test('releasing a target removes retained payloads and destroys target resources', async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();
    const instance = environment.hooks.instances.get('chart');
    const uniformBuffer = environment.hooks.createTrackedBuffer(instance, { size: 96, usage: 1 });
    const outputBuffer = environment.hooks.createTrackedBuffer(instance, { size: 32, usage: 1 });
    const paramsBuffer = environment.hooks.createTrackedBuffer(instance, { size: 16, usage: 1 });
    const source = { decimations: new Map([['navigator-detail-series:series:0', {
        outputBuffer, paramsBuffer, byteLength: 48,
    }]]) };
    instance.seriesBuffers.set('series', source);
    instance.targetResources.set('navigator-detail-series', [{ uniformBuffer }]);
    instance.lastPayloads.set('navigator-detail-series', {});
    instance.previewRenderKeys.set('navigator-detail-series', 'key');

    environment.api.releaseTarget('chart', 'navigator-detail-series');

    assert.equal(instance.lastPayloads.has('navigator-detail-series'), false);
    assert.equal(instance.targetResources.has('navigator-detail-series'), false);
    assert.equal(source.decimations.size, 0);
    assert.equal(instance.ownedGpuBytes, 0);
});

test('a lower budget applies immediately and evicts raw chunks even while persistently over budget', async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();
    const instance = environment.hooks.instances.get('chart');
    const persistent = environment.hooks.createTrackedBuffer(instance, { size: 8, usage: 1 });
    const raw = environment.hooks.createTrackedBuffer(instance, { size: 4, usage: 1 });
    instance.rawChunks.set('raw', {
        buffer: raw, pointBuffer: raw, byteLength: 4, lastUsed: 1, decimations: new Map(),
    });

    assert.equal(environment.api.setCacheBudget('chart', 6), false);
    assert.equal(raw.destroyed, true);
    assert.equal(instance.rawChunks.has('raw'), false);
    assert.equal(instance.cacheBudget, 6);
    assert.throws(
        () => environment.hooks.createTrackedBuffer(instance, { size: 1, usage: 1 }),
        /GPU memory budget/);

    environment.hooks.destroyTrackedBuffer(instance, persistent);
    environment.api.dispose('chart');
});

test('disposing an initialized chart removes its lifecycle epoch', async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();

    environment.api.dispose('chart');

    assert.equal(environment.hooks.lifecycleEpochs.has('chart'), false);
});

test('chunked series releases upload buffers and retains only summary storage', async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();
    const length = 1024;
    const overviewBytes = (4 + 1) * 48;
    const values = new Float32Array(length);

    const token = await environment.api.beginChunkedSeries('chart', 'series', 0, length);
    environment.api.appendChunkedSeries('chart', token, 0, values);
    await environment.api.processChunkedSeriesUpload('chart', token, 0, length);
    await environment.api.completeChunkedSeries('chart', token);

    const instance = environment.hooks.instances.get('chart');
    const source = instance.seriesBuffers.get(`series:0:${length}`);
    assert.equal(source.pyramid, true);
    assert.equal(source.buffer.size, overviewBytes);
    assert.equal(instance.ownedGpuBytes, overviewBytes);
    assert.equal(source.transientBuffer.destroyed, true);
    assert.equal(source.paramsBuffer.destroyed, true);
    assert.equal(instance.chunkedUploadSessions.size, 0);
    environment.api.dispose('chart');
});

test('production local renderer queries summary levels and uploads only boundary fragments', async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();
    const length = 32768;
    const reads = [];
    const token = await environment.api.beginChunkedSeries('chart', 'series', 0, length, 123n, async (offset, count) => {
        reads.push({ offset, count });
        return Float32Array.from({ length: count }, (_, i) => offset + i);
    });
    environment.api.appendChunkedSeries('chart', token, 0, new Float32Array(length));
    await environment.api.processChunkedSeriesUpload('chart', token, 0, length);
    await environment.api.completeChunkedSeries('chart', token);
    const instance = environment.hooks.instances.get('chart');
    const source = instance.seriesBuffers.get(`series:0:${length}`);
    let previous;
    for (const [begin, end, budget, expectedReads] of [
        [1000, 30000, 100, [{ offset: 1000, count: 157 }, { offset: 29829, count: 171 }]],
        [1157, 29829, 100, []],
        [700, 725, 32, [{ offset: 700, count: 25 }]],
    ]) {
        reads.length = 0;
        const start = instance.device.commands.length;
        const payload = {
            plot: { left: 0, top: 0, right: 1, bottom: 1 },
            series: [{ id: 'series', dataVersion: 0, length, sampleStep: 1 / length, viewFirst: begin, viewEnd: end, pointBudget: budget, axisMin: 0, axisMax: 1 }],
        };
        await environment.hooks.scheduleRender('chart', payload);
        assert.deepEqual(reads, expectedReads);
        const commands = instance.device.commands.slice(start);
        const dispatches = commands.filter(command => command.type === 'dispatch');
        assert.equal(dispatches.length, 1);
        const dispatch = dispatches[0];
        assert.equal(dispatch.pipeline.entryPoint, 'query');
        const output = source.decimations.get('series').outputBuffer;
        assert.equal(dispatch.bindGroup.entries[4].resource.buffer, output);
        const params = commands.find(command => command.type === 'write' && command.buffer === dispatch.bindGroup.entries[5].resource.buffer);
        const slots = end - begin <= budget ? 1 : 5;
        const jobs = output.size / (slots * 8);
        assert.deepEqual([...new Uint32Array(params.data)], [jobs, begin, slots, 0]);
        assert.equal(dispatch.count, Math.ceil(jobs / 64));
        const rawBuffer = dispatch.bindGroup.entries[1].resource.buffer;
        const rawWrite = commands.find(command => command.type === 'write' && command.buffer === rawBuffer);
        const expectedRaw = expectedReads.flatMap(({ offset, count }) => Array.from({ length: count }, (_, i) => offset + i));
        assert.deepEqual(rawWrite ? [...new Float32Array(rawWrite.data)] : [], expectedRaw);
        const copies = commands.filter(command => command.type === 'copy');
        assert.equal(copies.length > 0, end - begin > budget);
        for (const copy of copies) {
            assert.ok(source.pages.some(page => page.buffer === copy.source));
            assert.equal(copy.destination, dispatch.bindGroup.entries[0].resource.buffer);
        }
        const draws = commands.filter(command => command.type === 'draw');
        assert.deepEqual(draws.map(draw => draw.count), [(output.size / 8 - 1) * 6, (output.size / 8 - 1) * 18]);
        for (const draw of draws) {
            assert.equal(draw.bindGroup.entries[2].resource.buffer, output);
        }
        if (previous) assert.equal(previous.destroyed, true);
        previous = output;

        const allocations = instance.device.buffers.length;
        const cachedStart = instance.device.commands.length;
        await environment.hooks.scheduleRender('chart', payload);
        assert.deepEqual(reads, expectedReads);
        assert.equal(source.decimations.get('series').outputBuffer, output);
        assert.equal(instance.device.buffers.length, allocations);
        assert.equal(instance.device.commands.slice(cachedStart).some(command => command.type === 'dispatch' || command.type === 'copy'), false);
    }
    assert.equal(instance.rawRequests.size, 0);
    environment.api.dispose('chart');
    assert.equal(instance.ownedGpuBytes, 0);
});

test('local density uses physical target pixels without a server point budget', async () => {
    for (const dpr of [1, 1.25, 2, 3]) {
        const width = 1001;
        const environment = createEnvironment({ width, dpr });
        environment.api.initialize('chart', environment.helper('chart'));
        await settle();
        const length = 1000000;
        const token = await environment.api.beginChunkedSeries('chart', 'series', 0, length, 0n,
            async (_offset, count) => new Float32Array(count));
        environment.api.appendChunkedSeries('chart', token, 0, new Float32Array(length));
        await environment.api.processChunkedSeriesUpload('chart', token, 0, length);
        await environment.api.completeChunkedSeries('chart', token);
        const instance = environment.hooks.instances.get('chart');
        const source = instance.seriesBuffers.get(`series:0:${length}`);
        let previous;
        for (const plotWidth of [1, 0.5]) {
            await environment.hooks.scheduleRender('chart', {
                plot: { left: 0, top: 0, right: plotWidth, bottom: 1 },
                series: [{ id: 'series', length, sampleStep: 1 / length, axisMin: 0, axisMax: 1 }],
            });
            const view = source.decimations.get('series');
            const pixels = Math.round(width * dpr) * plotWidth;
            const budget = 5 * (Math.min(8192, Math.ceil(pixels * 4)) + 1);
            assert.equal(view.key, `0:${length}:${budget}`);
            assert.ok(view.outputBuffer.size / (5 * 8) >= Math.min(8192, pixels * 4) / 2);
            assert.ok(view.outputBuffer.size <= budget * 8);
            if (previous) assert.equal(previous.destroyed, true);
            previous = view.outputBuffer;
        }
        environment.api.dispose('chart');
        assert.equal(instance.ownedGpuBytes, 0);
    }
});

test('releasing a pyramid target during a boundary read prevents later allocations and commands', { timeout: 5000 }, async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();
    const length = 1024;
    const read = deferred();
    const entered = deferred();
    const token = await environment.api.beginChunkedSeries('chart', 'series', 0, length, 0n, (offset, count) => {
        entered.resolve({ offset, count });
        return read.promise;
    });
    environment.api.appendChunkedSeries('chart', token, 0, new Float32Array(length));
    await environment.api.processChunkedSeriesUpload('chart', token, 0, length);
    await environment.api.completeChunkedSeries('chart', token);
    const instance = environment.hooks.instances.get('chart');
    const source = instance.seriesBuffers.get(`series:0:${length}`);
    const resident = instance.ownedGpuBytes;
    const target = 'navigator-detail-series';
    let completed = false;
    const render = environment.hooks.scheduleRender('chart', {
        target,
        zoom: { left: 0, right: 10 / length },
        series: [{ id: 'series', length, sampleStep: 1 / length, viewFirst: 0, viewEnd: 10, pointBudget: 16 }],
    }).then(() => { completed = true; });
    assert.deepEqual(await entered.promise, { offset: 0, count: 10 });
    assert.equal(completed, false);
    environment.api.releaseTarget('chart', target);
    const allocations = instance.device.buffers.length;
    const commands = instance.device.commands.length;
    read.resolve(new Float32Array(10));
    await render;
    assert.equal(instance.device.buffers.length, allocations);
    assert.equal(instance.device.commands.length, commands);
    assert.equal(environment.hooks.instances.get('chart'), instance);
    assert.deepEqual(environment.failures, []);
    assert.equal(source.decimations.size, 0);
    assert.equal(instance.ownedGpuBytes, resident);
    assert.equal(instance.targetResources.has(target), false);
    assert.equal(environment.hooks.renderStates.size, 0);
    environment.api.dispose('chart');
    assert.equal(instance.ownedGpuBytes, 0);
});

test('a synthetic 10 GiB domain bounds paged allocations and releases resident storage', async () => {
  for (const [bindingMiB, bufferMiB] of [[128, 1024], [256, 1024], [512, 1024], [8, 1024], [128, 4]]) {
    const limit = Math.min(bindingMiB, bufferMiB) * 1024 ** 2;
    const length = 10 * 1024 ** 3 / 4;
    const environment = createEnvironment({ maxStorageBufferBindingSize: bindingMiB * 1024 ** 2, maxBufferSize: bufferMiB * 1024 ** 2 });
    environment.api.setCacheBudget('chart', 2 * 1024 ** 3);
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();
    const token = await environment.api.beginChunkedSeries('chart', 'series', 0, length);
    const instance = environment.hooks.instances.get('chart');
    const upload = instance.chunkedUploadSessions.get(token);
    assert.ok(upload.pages.length > 1);
    assert.equal(upload.pages[0].buffer.size, Math.floor(Math.min(limit, 16 * 1024 ** 2) / 48) * 48);
    assert.ok(upload.bytes >= 640 * 1024 ** 2 && upload.bytes < 641 * 1024 ** 2);
    assert.ok(environment.devices[0].buffers.every(buffer => buffer.size <= limit));
    assert.ok(instance.ownedGpuBytes < 2 * 1024 ** 3);
    // Skip raw upload: this verifies allocation/lifetime, not parent numerical contents.
    upload.writtenLength = length;
    await environment.api.completeChunkedSeries('chart', token);
    assert.equal(instance.ownedGpuBytes, upload.bytes);
    assert.ok(environment.devices[0].buffers.every(buffer => buffer.size <= limit));
    environment.api.synchronizeSeries('chart', []);
    assert.equal(instance.ownedGpuBytes, 0);
    assert.ok(environment.devices[0].buffers.every(buffer => buffer.destroyed));
    environment.api.dispose('chart');
  }
});

test('partial multi-page allocation failure releases every page within the tracked budget', async () => {
    const environment = createEnvironment({ maxStorageBufferBindingSize: 512 * 1024 ** 2 });
    environment.api.setCacheBudget('chart', 40 * 1024 ** 2);
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();
    const instance = environment.hooks.instances.get('chart');
    await assert.rejects(environment.api.beginChunkedSeries('chart', 's', 0, 10 * 1024 ** 3 / 4), /budget/);
    assert.equal(instance.ownedGpuBytes, 0);
    assert.ok(environment.devices[0].buffers.length >= 2);
    assert.ok(environment.devices[0].buffers.every(buffer => buffer.destroyed));
    environment.api.dispose('chart');
});

test('repeated local and remote dispose/reinitialize cycles release completed pyramids and idle uploads', async () => {
    const environment = createEnvironment();
    for (let cycle = 0; cycle < 3; cycle++) {
        environment.api.initialize('chart', environment.helper('chart'));
        await settle();
        const length = 1024;
        const token = await environment.api.beginChunkedSeries('chart', 'series', 0, length, 0n, async (_, count) => new Float32Array(count));
        environment.api.appendChunkedSeries('chart', token, 0, new Float32Array(length));
        await environment.api.processChunkedSeriesUpload('chart', token, 0, length);
        await environment.api.completeChunkedSeries('chart', token);
        await environment.hooks.scheduleRender('chart', {
            zoom: { left: 0, right: 1 },
            series: [{ id: 'series', length, sampleStep: 1 / length, pointBudget: 16, axisMin: 0, axisMax: 1 }],
        });
        const pending = await environment.api.beginChunkedSeries('chart', 'pending', 0, length);
        const local = environment.hooks.instances.get('chart');
        assert.ok(local.seriesBuffers.get(`series:0:${length}`).decimations.get('series').outputBuffer);
        assert.equal(local.chunkedUploadSessions.get(pending).writtenLength, 0);
        assert.ok(local.ownedGpuBytes > 0);
        environment.api.dispose('chart');
        assert.equal(local.ownedGpuBytes, 0);
        assert.ok(local.device.buffers.every(buffer => buffer.destroyed));

        environment.api.initialize('chart', environment.helper('chart'));
        await settle();
        await environment.hooks.scheduleRender('chart', {
            series: [{ id: 'series', remotePoints: new Float32Array([0, 1, 1, 2]), axisMin: 0, axisMax: 3 }],
        });
        const remote = environment.hooks.instances.get('chart');
        assert.ok(remote.ownedGpuBytes > 0);
        environment.api.dispose('chart');
        assert.equal(remote.ownedGpuBytes, 0);
        assert.ok(remote.device.buffers.every(buffer => buffer.destroyed));
        assert.equal(environment.hooks.renderStates.size, 0);
    }
});

test('chunked series accepts source-generated MemoryView spans synchronously', async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();
    const values = new Float32Array([1, 2, 3, 4]);
    const memoryView = {
        _unsafe_create_view() { return new Uint8Array(values.buffer); },
    };

    const token = await environment.api.beginChunkedSeries('chart', 'series', 0, values.length);
    environment.api.appendChunkedSeries('chart', token, 0, memoryView, values.byteLength);
    await environment.api.processChunkedSeriesUpload('chart', token, 0, values.length);
    await environment.api.completeChunkedSeries('chart', token);

    const instance = environment.hooks.instances.get('chart');
    assert.equal(instance.seriesBuffers.has(`series:0:${values.length}`), true);
});

test('chunked series requests raw detail from its .NET provider', async () => {
    const environment = createEnvironment();
    const calls = [];
    const helper = {
        invokeMethodAsync(method, ...args) {
            calls.push([method, ...args]);
            return Promise.resolve();
        },
    };
    environment.api.initialize('chart', helper);
    await settle();
    const instance = environment.hooks.instances.get('chart');
    instance.uploadGenerations.set('series', 1);
    const source = {
        id: 'series', version: 0, length: 3, chartId: 'chart', generation: 1, chunked: true,
    };

    environment.hooks.getRawRenderItems(
        instance, source, { SampleStep: 1 / 3 }, { Zoom: { Left: 0, Right: 1 } },
        { plotLeft: 0, plotWidth: 100, plotTop: 0, plotBottom: 100, plotHeight: 100 },
        instance.device.createCommandEncoder(), 'series', new Set());

    assert.equal(calls[0][0], 'ProvideSeriesChunk');
    assert.deepEqual(calls[0].slice(1, 4), ['series', 0, 3]);
    const requestId = calls[0][4];
    environment.api.appendSeriesChunk('chart', requestId, 0, new Float32Array([1, 2, 3]));
    assert.equal(instance.rawChunks.get('series:0:0').length, 3);
});

test('render scheduling retains only the latest pending payload per target', async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    const first = environment.hooks.scheduleRender('chart', { Marker: 'first' });
    environment.hooks.scheduleRender('chart', { Marker: 'second' });
    environment.hooks.scheduleRender('chart', { Marker: 'latest' });
    await first;

    const instance = environment.hooks.instances.get('chart');
    assert.equal(instance.lastPayloads.get('series').Marker, 'latest');
    assert.ok(environment.devices[0].submissions <= 2);
});

test('canvas context is configured once and unconfigured when replaced', async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();
    const instance = environment.hooks.instances.get('chart');
    const createCanvas = () => {
        const context = {
            configureCalls: 0,
            unconfigureCalls: 0,
            configure() { this.configureCalls++; },
            unconfigure() { this.unconfigureCalls++; },
        };
        return { context, getContext() { return context; } };
    };
    const first = createCanvas();
    const second = createCanvas();

    assert.equal(environment.hooks.getCanvasContext(instance, 'series', first), first.context);
    assert.equal(environment.hooks.getCanvasContext(instance, 'series', first), first.context);
    assert.equal(first.context.configureCalls, 1);

    assert.equal(environment.hooks.getCanvasContext(instance, 'series', second), second.context);
    assert.equal(first.context.unconfigureCalls, 1);
    assert.equal(second.context.configureCalls, 1);

    instance.previewRenderKeys.set('series', 'preview');
    environment.hooks.releaseCanvasContext(instance, 'series');
    assert.equal(second.context.unconfigureCalls, 1);
    assert.equal(instance.canvasContexts.has('series'), false);
    assert.equal(instance.previewRenderKeys.has('series'), false);
});

test('series zoom uses sample period instead of stretching each series to its length', () => {
    const environment = createEnvironment();
    const plot = { plotLeft: 10, plotWidth: 600 };
    const payload = { Zoom: { Left: 0, Right: 1 } };

    const fast = environment.hooks.getZoomInfo(payload, { SampleStep: 0.5 / 60 }, 120, plot);
    const slow = environment.hooks.getZoomInfo(payload, { SampleStep: 1 / 60 }, 60, plot);

    assert.equal(fast.first, 0);
    assert.equal(fast.segmentCount, 119);
    assert.equal(fast.zoomedLeft, 10);
    assert.equal(fast.dx, 5);
    assert.equal(slow.first, 0);
    assert.equal(slow.segmentCount, 59);
    assert.equal(slow.zoomedLeft, 10);
    assert.equal(slow.dx, 10);
});

test('time-domain zoom clips a series without changing its sample spacing', () => {
    const environment = createEnvironment();
    const plot = { plotLeft: 0, plotWidth: 400 };

    const zoom = environment.hooks.getZoomInfo(
        { Zoom: { Left: 0.25, Right: 0.75 } }, { SampleStep: 0.1 }, 6, plot);

    assert.equal(zoom.first, 2);
    assert.equal(zoom.segmentCount, 3);
    assert.equal(zoom.zoomedLeft, -40);
    assert.equal(zoom.dx, 80);
});

test('overview zoom uses the same temporal endpoint as direct rendering', () => {
    const environment = createEnvironment();
    const plot = { plotLeft: 10, plotWidth: 600 };
    const payload = { Zoom: { Left: 0, Right: 1 } };
    const series = { SampleStep: 1 / 60 };
    const source = { length: 60, overviewBucketCount: 1 };

    const direct = environment.hooks.getZoomInfo(payload, series, source.length, plot);
    const overview = environment.hooks.getOverviewZoom(payload, series, source, plot);
    const directLastX = direct.zoomedLeft + direct.segmentCount * direct.dx;
    const overviewLastX = overview.zoomedLeft + (59 / 256 - overview.xOrigin) * overview.dx;

    assert.equal(directLastX, 600);
    assert.equal(overviewLastX, directLastX);
});

test('invalid sample periods do not produce render windows', () => {
    const environment = createEnvironment();
    const payload = { Zoom: { Left: 0, Right: 1 } };

    assert.equal(environment.hooks.getTimeWindow(payload, { SampleStep: 0 }, 10), null);
    assert.equal(environment.hooks.getTimeWindow(payload, { SampleStep: -1 }, 10), null);
});
