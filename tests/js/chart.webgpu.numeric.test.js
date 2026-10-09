const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const { summarize, project: projectSummary } = require('./chart.pyramid.test.js');

test(process.env.NEXUS_WEBGPU_COMPILE_ONLY ? 'all WebGPU shader modules compile' : 'GPU reduction matches canonical projection fixtures', { skip: !process.env.NEXUS_WEBGPU_MODULE }, async () => {
    const { create, globals } = await import(process.env.NEXUS_WEBGPU_MODULE);
    Object.assign(globalThis, globals);
    const gpu = create(process.env.NEXUS_WEBGPU_COMPILE_ONLY ? ['backend=null'] : []);
    const adapter = await gpu.requestAdapter();
    assert.ok(adapter, 'A GPU adapter is required for numerical verification');
    const device = await adapter.requestDevice({ requiredLimits: { maxStorageBuffersPerShaderStage: 5 } });
    const context = { window: {}, ...globals, Float32Array, Uint32Array };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../src/Nexus.UI/src/charts/chart.webgpu.shaders.js'), 'utf8'), context);
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../src/Nexus.UI/src/charts/chart.webgpu.pyramid.js'), 'utf8'), context);
    const shaders = context.window.__nexusChartWebGpu;

    function project(values) {
        let minimum;
        let maximum;
        let firstGap;
        let runs = 0;
        for (let i = 0; i < values.length; i++) {
            if (!Number.isFinite(values[i])) {
                firstGap ??= i;
                if (i === 0 || Number.isFinite(values[i - 1])) runs++;
            } else {
                if (minimum === undefined || values[i] < values[minimum]) minimum = i;
                if (maximum === undefined || values[i] > values[maximum]) maximum = i;
            }
        }
        const indices = minimum === undefined || runs >= 2
            ? [firstGap]
            : [...new Set([0, values.length - 1, minimum, maximum, ...(firstGap === undefined ? [] : [firstGap])])].sort((a, b) => a - b);
        const points = indices.map(index => [index, minimum === undefined || runs >= 2 || !Number.isFinite(values[index]) ? NaN : values[index]]);
        while (points.length < 5) points.push(points.at(-1));
        return points.flat();
    }

    try {
        for (const name of ['shader', 'overviewShader', 'decimationShader', 'pointDecimationShader', 'rangeShader', 'baseSummaryShader', 'parentSummaryShader', 'querySummaryShader']) {
            const module = device.createShaderModule({ code: shaders[name] });
            const info = await module.getCompilationInfo();
            assert.deepEqual(info.messages.filter(message => message.type === 'error').map(message => message.message), [], name);
        }
        for (const [name, entryPoint] of [['baseSummaryShader', 'base'], ['parentSummaryShader', 'parent'], ['querySummaryShader', 'query']]) {
            await device.createComputePipelineAsync({ layout: 'auto', compute: { module: device.createShaderModule({ code: shaders[name] }), entryPoint } });
        }
        if (process.env.NEXUS_WEBGPU_COMPILE_ONLY) return;
        // Exercise the production upload, parent-build and query functions, not a
        // second shader implementation. Read back only the bounded drawing output.
        const instance = { device, chunkedUploadSessions: new Map(), seriesBuffers: new Map(), uploadToken: 0, disposed: false };
        Object.assign(shaders, {
            getInstance: async () => instance,
            createTrackedBuffer: (_instance, descriptor) => {
                assert.ok(descriptor.size <= instance.device.limits.maxStorageBufferBindingSize, `oversized allocation ${descriptor.size}`);
                return device.createBuffer(descriptor);
            },
            destroyTrackedBuffer: (_instance, buffer) => buffer?.destroy(),
            calculateSeriesRangeAsync: async () => ({ hasValue: false }),
            getSeriesKey: (id, version, length) => `${id}:${version}:${length}`,
            valueOf: (object, name) => object[name[0].toLowerCase() + name.slice(1)],
            cancellationError: message => new Error(message),
            getTimeWindow: (_payload, series) => ({ first: series.viewFirst, last: series.viewEnd - 1, indexLeft: series.viewFirst, indexRange: series.viewEnd - series.viewFirst }),
        });
        const pyramidFixtures = [
            Float32Array.from({ length: 4103 }, (_, i) => i % 517 < 3 ? NaN : i % 41 - 20),
            new Float32Array(4103).fill(7),
            Float32Array.from({ length: 4103 }, (_, i) => i % 2 ? 0 : -0),
            Float32Array.from({ length: 4103 }, (_, i) => i % 7 === 0 ? Infinity : i % 7 === 3 ? -Infinity : i % 7 === 5 ? NaN : i),
            new Float32Array(4103).fill(Infinity),
        ];
        for (const cap of [device.limits.maxStorageBufferBindingSize, 4096]) {
        instance.device = new Proxy(device, { get(target, key) {
            if (key === 'limits') return { maxBufferSize: cap, maxStorageBufferBindingSize: cap };
            const value = Reflect.get(target, key, target);
            return typeof value === 'function' ? value.bind(target) : value;
        } });
        for (const origin of [0n, 123n, 255n, 1023n, 9007199254740993123n]) {
          for (const raw of pyramidFixtures) {
            const input = cap === 4096 ? Float32Array.from({ length: 65543 }, (_, i) => raw[i % raw.length]) : raw;
            const token = await shaders.beginChunkedSeriesAsync('chart', 's', 0, input.length, origin, async (offset, count) => input.slice(offset, offset + count));
            const upload = instance.chunkedUploadSessions.get(token);
            if (cap === 4096) assert.ok(upload.pages.length > 1);
            // Deliberately split a base bucket over upload calls.
            for (const [start, end] of [[0, 731], [731, 4096], [4096, input.length]]) {
                upload.pendingValues = input.subarray(start, end);
                await shaders.processChunkedSeriesUploadAsync('chart', token, start, end - start);
            }
            await shaders.completeChunkedSeriesAsync('chart', token);
            const source = instance.seriesBuffers.get(`s:0:${input.length}`);
            for (const [begin, end, budget] of [[0, input.length, 40], [17, input.length - 102, 20], [700, 725, 32], [1000, 1801, 80], [21001, Math.min(43019, input.length), 40]].filter(([begin,end]) => begin < end)) {
                const series = { viewFirst: begin, viewEnd: end, pointBudget: budget };
                const item = await shaders.getPyramidRenderItem(instance, source, series, {}, { plotLeft: 0, plotWidth: 100 }, 'main');
                const output = item.seriesBuffer.pointBuffer;
                const readback = device.createBuffer({ size: output.size, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
                const encoder = device.createCommandEncoder();
                encoder.copyBufferToBuffer(output, 0, readback, 0, output.size);
                device.queue.submit([encoder.finish()]);
                await readback.mapAsync(GPUMapMode.READ);
                const actual = [...new Float32Array(readback.getMappedRange())];
                const plan = shaders.planQuery(origin, input.length, begin, end, budget, source.levels);
                const expected = [];
                for (let start = begin; start < end;) {
                    const stop = Math.min(end, Number(((origin + BigInt(start)) / plan.stride + 1n) * plan.stride - origin));
                    const points = projectSummary(summarize(input.subarray(start, stop), start));
                    while (points.length < plan.slots) points.push(points.at(-1));
                    expected.push(...points.flatMap(([index, value]) => [index - begin, value]));
                    start = stop;
                }
                assert.deepEqual(actual, expected, `pyramid cap=${cap} origin=${origin} view=${begin}:${end}`);
                readback.unmap(); readback.destroy();
            }
            for (const view of source.decimations.values()) view.outputBuffer.destroy();
            source.pages.forEach(page => page.buffer.destroy()); instance.seriesBuffers.clear();
          }
        }
        }
        const fixtures = [
            [3, 1, 8, 4], [5, NaN, 1, 8, 4], [NaN, 1, 8, NaN],
            [NaN, Infinity, -Infinity], [1], [-0, 0, -0, 0],
            Array.from({ length: 256 }, (_, i) => i === 1 || i === 128 ? -5 : i === 3 || i === 192 ? 8 : 0),
            Array.from({ length: 129 }, (_, i) => i < 65 ? NaN : i),
        ];
        for (const input of fixtures) {
            const values = Float32Array.from(input);
            for (const [name, entryPoint, points] of [
                ['overviewShader', 'reduceOverview', false],
                ['decimationShader', 'decimate', false],
                ['pointDecimationShader', 'decimatePoints', true],
            ]) {
                const data = points ? Float32Array.from(input.flatMap((value, i) => [i, value])) : values;
                const source = device.createBuffer({ size: data.byteLength, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST });
                const output = device.createBuffer({ size: 40, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC });
                const params = device.createBuffer({ size: 16, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
                const readback = device.createBuffer({ size: 40, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
                device.queue.writeBuffer(source, 0, data);
                device.queue.writeBuffer(params, 0, new Uint32Array(name === 'overviewShader' ? [0, values.length, 0, 0] : [0, values.length, 1, values.length]));
                const pipeline = await device.createComputePipelineAsync({ layout: 'auto', compute: { module: device.createShaderModule({ code: shaders[name] }), entryPoint } });
                const bindGroup = device.createBindGroup({ layout: pipeline.getBindGroupLayout(0), entries: [source, output, params].map((buffer, binding) => ({ binding, resource: { buffer } })) });
                const encoder = device.createCommandEncoder();
                const pass = encoder.beginComputePass();
                pass.setPipeline(pipeline);
                pass.setBindGroup(0, bindGroup);
                pass.dispatchWorkgroups(1);
                pass.end();
                encoder.copyBufferToBuffer(output, 0, readback, 0, 40);
                device.queue.submit([encoder.finish()]);
                await readback.mapAsync(GPUMapMode.READ);
                const actual = [...new Float32Array(readback.getMappedRange())];
                const expected = project(values);
                if (name === 'overviewShader') {
                    for (let i = 0; i < expected.length; i += 2) expected[i] /= 256;
                }
                assert.deepEqual(actual, expected, `${name}: ${input}`);
                readback.unmap();
                for (const buffer of [source, output, params, readback]) buffer.destroy();
            }
        }
    } finally {
        device.destroy();
    }
});
