const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const scriptNames = [
    'chart.webgpu.shaders.js',
    'chart.webgpu.lifecycle.js',
    'chart.webgpu.data.js',
    'chart.webgpu.js',
];
const scripts = scriptNames.map(name => ({
    name,
    source: fs.readFileSync(path.join(__dirname, '../../src/Nexus/wwwroot/js', name), 'utf8'),
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
        submissions: 0,
        createShaderModule() { return {}; },
        createRenderPipeline() { return { getBindGroupLayout() { return {}; } }; },
        createComputePipeline() { return { getBindGroupLayout() { return {}; } }; },
        createBuffer({ size }) {
            const buffer = createBuffer(size, options);
            this.buffers.push(buffer);
            return buffer;
        },
        createBindGroup() { return {}; },
        createCommandEncoder() {
            return {
                beginComputePass() {
                    return { setPipeline() {}, setBindGroup() {}, dispatchWorkgroups() {}, end() {} };
                },
                beginRenderPass() {
                    return { setPipeline() {}, setScissorRect() {}, setBindGroup() {}, draw() {}, end() {} };
                },
                copyBufferToBuffer() {},
                finish() { return {}; },
            };
        },
        queue: { writeBuffer() {}, submit() { device.submissions++; }, async onSubmittedWorkDone() {} },
        destroy() { this.destroyed = true; },
        lose(info = { message: 'test loss' }) { lost.resolve(info); },
    };
    return device;
}

function createEnvironment(options = {}) {
    const devices = [];
    const workers = [];
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
        Worker: class {
            constructor(url) {
                this.url = url;
                this.messages = [];
                this.terminated = false;
                workers.push(this);
            }
            postMessage(message) { this.messages.push(message); }
            terminate() { this.terminated = true; }
        },
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
                            clientWidth: 100,
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
        workers,
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

test('invalid chunk uploads destroy partial buffers and fail the chart', async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();

    const token = await environment.api.beginSeriesUpload('chart', 'series', 1, 4);
    const stream = bytes => ({ arrayBuffer: async () => Uint8Array.from(bytes).buffer });

    const upload = environment.hooks.instances.get('chart').uploadSessions.get(token);
    await assert.rejects(
        environment.api.appendSeriesUpload('chart', token, 4, stream([0, 0, 0, 0])),
        /expected byte offset 0/);
    await settle();

    assert.equal(upload.buffer.destroyed, true);
    assert.equal(environment.hooks.instances.has('chart'), false);
    assert.equal(environment.failures[0].title, 'WebGPU upload failed');
});

test('terminal upload failures invalidate the instance and notify the chart', async () => {
    const environment = createEnvironment({ maxStorageBufferBindingSize: 8 });
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();

    await assert.rejects(environment.api.beginSeriesUpload('chart', 'series', 1, 3), /exceeding the GPU storage buffer limit/);
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
    instance.rawCacheBytes = 8;
    instance.ownedGpuBytes = 8;

    environment.hooks.evictRawChunks(instance, 4);

    assert.equal(first.buffer.destroyed, true);
    assert.equal(instance.rawChunks.has('first'), false);
    assert.equal(instance.rawChunks.has('second'), true);
    assert.equal(instance.rawCacheBytes, 4);
});

test('raw chunks selected earlier in a frame remain pinned under later cache pressure', async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();
    const instance = environment.hooks.instances.get('chart');
    const firstBuffer = createBuffer(12);
    const firstChunk = {
        id: 'first', buffer: firstBuffer, pointBuffer: firstBuffer, byteLength: 12,
        offset: 0, length: 3, lastUsed: 1, dataMode: 0, decimations: new Map(),
    };
    instance.rawChunks.set('first:1:0', firstChunk);
    instance.rawCacheBytes = 12;
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

test('synthetic cancellation waits for in-flight range work before destroying buffers', async () => {
    const environment = createEnvironment({ deferMapAsync: true });
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();
    const instance = environment.hooks.instances.get('chart');
    const firstGeneration = environment.api.generateSyntheticSeries('chart', 'series', 1, 2, 'Sine');
    await settle();
    const requestId = instance.generationJobs.get('series').requestId;
    const transientBuffer = environment.devices[0].buffers[0];
    const overviewBuffer = environment.devices[0].buffers[1];
    instance.workerCallbacks.get(requestId).onmessage({
        data: { requestId, offset: 0, values: new Float32Array([1, 2]) },
    });
    await settle();

    const replacement = environment.api.generateSyntheticSeries('chart', 'series', 2, 2, 'Sine');
    replacement.catch(() => {});
    await settle();
    assert.equal(transientBuffer.destroyed, false);
    assert.equal(overviewBuffer.destroyed, false);

    environment.mapRequests.shift()();
    await assert.rejects(firstGeneration, /superseded/);
    await settle();
    assert.equal(transientBuffer.destroyed, true);
    assert.equal(overviewBuffer.destroyed, true);
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

test('fill shader multiplies fill opacity by series alpha', () => {
    const shaderSource = scripts.find(script => script.name === 'chart.webgpu.shaders.js').source;
    assert.match(shaderSource, /uniforms\.color\.a \* uniforms\.fillOpacity/);
});

test('gap-preserving reduction reserves three points per bucket plus endpoints', () => {
    const environment = createEnvironment();

    assert.equal(environment.hooks.reducedPointsPerBucket, 3);
    assert.equal(environment.hooks.getReducedOutputLength(8), 26);
});

test('every reduction shader blanks buckets containing multiple NaN runs', () => {
    const shaderSource = scripts.find(script => script.name === 'chart.webgpu.shaders.js').source;
    assert.equal((shaderSource.match(/nanRunCounts\[0\] >= 2u/g) ?? []).length, 3);
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
    instance.auxiliaryBytes = 48;

    environment.api.releaseTarget('chart', 'navigator-detail-series');

    assert.equal(instance.lastPayloads.has('navigator-detail-series'), false);
    assert.equal(instance.targetResources.has('navigator-detail-series'), false);
    assert.equal(source.decimations.size, 0);
    assert.equal(instance.ownedGpuBytes, 0);
    assert.equal(instance.auxiliaryBytes, 0);
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

test('synthetic work reuses one worker per chart and terminates it on disposal', async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();
    const instance = environment.hooks.instances.get('chart');

    const first = environment.hooks.getSyntheticWorker(instance);
    const second = environment.hooks.getSyntheticWorker(instance);

    assert.equal(first, second);
    assert.equal(environment.workers.length, 1);
    environment.api.dispose('chart');
    assert.equal(first.terminated, true);
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

test('synthetic overview uses the same temporal endpoint as direct rendering', () => {
    const environment = createEnvironment();
    const plot = { plotLeft: 10, plotWidth: 600 };
    const payload = { Zoom: { Left: 0, Right: 1 } };
    const series = { SampleStep: 1 / 60 };
    const source = { length: 60, overviewBucketCount: 1 };

    const direct = environment.hooks.getZoomInfo(payload, series, source.length, plot);
    const overview = environment.hooks.getSyntheticOverviewZoom(payload, series, source, plot);
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
