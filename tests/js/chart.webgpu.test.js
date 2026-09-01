const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const script = fs.readFileSync(
    path.join(__dirname, '../../src/Nexus/wwwroot/js/chart.webgpu.js'),
    'utf8');

function deferred() {
    let resolve;
    const promise = new Promise(value => resolve = value);
    return { promise, resolve };
}

function createBuffer(size) {
    return {
        size,
        destroyed: false,
        mapState: 'unmapped',
        destroy() { this.destroyed = true; },
    };
}

function createDevice() {
    const lost = deferred();
    const device = {
        limits: {
            maxBufferSize: 1024 * 1024 * 1024,
            maxStorageBufferBindingSize: 1024 * 1024 * 1024,
        },
        lost: lost.promise,
        destroyed: false,
        buffers: [],
        createShaderModule() { return {}; },
        createRenderPipeline() { return { getBindGroupLayout() { return {}; } }; },
        createComputePipeline() { return { getBindGroupLayout() { return {}; } }; },
        createBuffer({ size }) {
            const buffer = createBuffer(size);
            this.buffers.push(buffer);
            return buffer;
        },
        queue: { writeBuffer() {} },
        destroy() { this.destroyed = true; },
        lose(info = { message: 'test loss' }) { lost.resolve(info); },
    };
    return device;
}

function createEnvironment(options = {}) {
    const devices = [];
    const workers = [];
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

            const device = createDevice();
            devices.push(device);
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
                if (!canvases.has(id))
                    canvases.set(id, {});
                return canvases.get(id);
            },
        },
        __nexusChartWebGpuTestHooks: hooks,
    };
    context.window = context;
    context.globalThis = context;
    vm.runInNewContext(script, context, { filename: 'chart.webgpu.js' });

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

test('chunk uploads reject gaps and abort destroys partial buffers', async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();

    const token = await environment.api.beginSeriesUpload('chart', 'series', 1, 4);
    const stream = bytes => ({ arrayBuffer: async () => Uint8Array.from(bytes).buffer });

    await assert.rejects(
        environment.api.appendSeriesUpload('chart', token, 4, stream([0, 0, 0, 0])),
        /expected byte offset 0/);

    const upload = environment.hooks.instances.get('chart').uploadSessions.get(token);
    environment.api.abortSeriesUpload('chart', token);
    assert.equal(upload.buffer.destroyed, true);
    assert.equal(environment.hooks.instances.get('chart').uploadSessions.size, 0);
});

test('raw-detail cache evicts least recently used chunks to its budget', async () => {
    const environment = createEnvironment();
    environment.api.initialize('chart', environment.helper('chart'));
    await settle();
    const instance = environment.hooks.instances.get('chart');
    instance.cacheBudget = 8;
    const first = { buffer: createBuffer(4), pointBuffer: null, byteLength: 4, lastUsed: 1, decimations: new Map() };
    first.pointBuffer = first.buffer;
    const second = { buffer: createBuffer(4), pointBuffer: null, byteLength: 4, lastUsed: 2, decimations: new Map() };
    second.pointBuffer = second.buffer;
    instance.rawChunks.set('first', first);
    instance.rawChunks.set('second', second);
    instance.rawCacheBytes = 8;

    environment.hooks.evictRawChunks(instance, 4);

    assert.equal(first.buffer.destroyed, true);
    assert.equal(instance.rawChunks.has('first'), false);
    assert.equal(instance.rawChunks.has('second'), true);
    assert.equal(instance.rawCacheBytes, 4);
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

test('gap-preserving reduction reserves three points per bucket plus endpoints', () => {
    const environment = createEnvironment();

    assert.equal(environment.hooks.reducedPointsPerBucket, 3);
    assert.equal(environment.hooks.getReducedOutputLength(8), 26);
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
