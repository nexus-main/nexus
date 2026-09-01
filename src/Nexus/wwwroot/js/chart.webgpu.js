(function () {
    const instances = new Map();
    const pendingInstances = new Map();
    const uniformBufferSize = 96;
    const fillVerticesPerSegment = 6;
    const lineVerticesPerSegment = 18;
    const decimationWorkgroupSize = 64;
    const decimationFactor = 4;
    const decimationBucketsPerPixel = 2;
    const maxDecimationBuckets = 8192;
    const rangeWorkgroupSize = 256;
    const maxRangeWorkgroups = 1024;

    const shader = `
struct Uniforms {
    viewport: vec2f,
    _pad0: vec2f,
    plot: vec4f,
    axis: vec2f,
    xParams: vec2f,
    zeroY: f32,
    lineWidth: f32,
    fillOpacity: f32,
    _pad1: f32,
    color: vec4f,
    startIndex: u32,
    mode: u32,
    dataMode: u32,
    _pad2: u32,
};

struct VertexOut {
    @builtin(position) position: vec4f,
    @location(0) color: vec4f,
};

@group(0) @binding(0) var<uniform> uniforms: Uniforms;
@group(0) @binding(1) var<storage, read> values: array<f32>;
@group(0) @binding(2) var<storage, read> points: array<vec2f>;

fn toNdc(point: vec2f) -> vec4f {
    return vec4f(point.x / uniforms.viewport.x * 2.0 - 1.0, 1.0 - point.y / uniforms.viewport.y * 2.0, 0.0, 1.0);
}

fn dataPoint(index: u32) -> vec2f {
    var x: f32;
    var value: f32;

    if (uniforms.dataMode == 1u) {
        x = points[index].x;
        value = points[index].y;
    } else {
        x = f32(index - uniforms.startIndex);
        value = values[index];
    }

    return vec2f(
        uniforms.xParams.x + uniforms.xParams.y * x,
        uniforms.plot.w - ((value - uniforms.axis.x) / uniforms.axis.y) * (uniforms.plot.w - uniforms.plot.y));
}

fn dataValue(index: u32) -> f32 {
    var value: f32;

    if (uniforms.dataMode == 1u) {
        value = points[index].y;
    } else {
        value = values[index];
    }

    return value;
}

fn isNan(x: f32) -> bool {
    // WGSL has no isnan() builtin, and "x != x" is unreliable under fast-math/indeterminate values.
    // Bit-pattern test (exponent all 1s, mantissa non-zero) is unambiguous across drivers.
    let bits = bitcast<u32>(x);
    return (bits & 0x7f800000u) == 0x7f800000u && (bits & 0x007fffffu) != 0u;
}

fn emptyVertex() -> VertexOut {
    var out: VertexOut;
    out.position = vec4f(0.0, 0.0, 0.0, 1.0);
    out.color = vec4f(0.0);
    return out;
}

fn fillVertex(a: vec2f, b: vec2f, local: u32) -> vec2f {
    let a0 = vec2f(a.x, uniforms.zeroY);
    let b0 = vec2f(b.x, uniforms.zeroY);

    let crossing = (a.y - uniforms.zeroY) * (b.y - uniforms.zeroY) < 0.0;

    var c = b;
    if (crossing) {
        let t = (uniforms.zeroY - a.y) / (b.y - a.y);
        c = vec2f(a.x + t * (b.x - a.x), uniforms.zeroY);
    }

    switch local {
        case 0u: { return a0; }
        case 1u: { return a; }
        case 2u: { return select(b, c, crossing); }
        case 3u: { return select(a0, c, crossing); }
        case 4u: { return b; }
        default: { return b0; }
    }
}

fn lineVertex(a: vec2f, b: vec2f, local: u32) -> VertexOut {
    let delta = b - a;
    let segmentLength = length(delta);

    if (segmentLength <= 0.0) {
        return emptyVertex();
    }

    let half = uniforms.lineWidth / 2.0;
    let fringe = 0.5;
    let unit = vec2f(-delta.y / segmentLength, delta.x / segmentLength);
    let normal = unit * half;
    let outer = unit * (half + fringe);
    let transparent = vec4f(uniforms.color.rgb, 0.0);

    let p0 = a + normal;
    let p1 = a - normal;
    let p2 = b + normal;
    let p3 = b - normal;
    let o0 = a + outer;
    let o1 = a - outer;
    let o2 = b + outer;
    let o3 = b - outer;

    var point: vec2f;
    var color = uniforms.color;

    switch local {
        case 0u: { point = p0; }
        case 1u: { point = p1; }
        case 2u: { point = p2; }
        case 3u: { point = p2; }
        case 4u: { point = p1; }
        case 5u: { point = p3; }
        case 6u: { point = o0; color = transparent; }
        case 7u: { point = p0; }
        case 8u: { point = o2; color = transparent; }
        case 9u: { point = o2; color = transparent; }
        case 10u: { point = p0; }
        case 11u: { point = p2; }
        case 12u: { point = p1; }
        case 13u: { point = o1; color = transparent; }
        case 14u: { point = p3; }
        case 15u: { point = p3; }
        case 16u: { point = o1; color = transparent; }
        default: { point = o3; color = transparent; }
    }

    var out: VertexOut;
    out.position = toNdc(point);
    out.color = color;
    return out;
}

@vertex
fn vertexMain(@builtin(vertex_index) vertexIndex: u32) -> VertexOut {
    let verticesPerSegment = select(18u, 6u, uniforms.mode == 0u);
    let segment = vertexIndex / verticesPerSegment;
    let local = vertexIndex % verticesPerSegment;
    let index = uniforms.startIndex + segment;
    let valueA = dataValue(index);
    let valueB = dataValue(index + 1u);

    if (isNan(valueA) || isNan(valueB) || uniforms.axis.y == 0.0) {
        return emptyVertex();
    }

    let a = dataPoint(index);
    let b = dataPoint(index + 1u);

    if (uniforms.mode == 0u) {
        var out: VertexOut;
        out.position = toNdc(fillVertex(a, b, local));
        out.color = vec4f(uniforms.color.rgb, uniforms.fillOpacity);
        return out;
    }

    return lineVertex(a, b, local);
}

@fragment
fn fragmentMain(in: VertexOut) -> @location(0) vec4f {
    return in.color;
}
`;

    const decimationShader = `
struct Params {
    first: u32,
    visibleLength: u32,
    bucketCount: u32,
    sourceLength: u32,
};

@group(0) @binding(0) var<storage, read> source: array<f32>;
@group(0) @binding(1) var<storage, read_write> output: array<vec2f>;
@group(0) @binding(2) var<uniform> params: Params;

var<workgroup> minimums: array<f32, ${decimationWorkgroupSize}>;
var<workgroup> maximums: array<f32, ${decimationWorkgroupSize}>;
var<workgroup> minimumIndices: array<u32, ${decimationWorkgroupSize}>;
var<workgroup> maximumIndices: array<u32, ${decimationWorkgroupSize}>;
var<workgroup> valid: array<u32, ${decimationWorkgroupSize}>;
var<workgroup> nanSeen: array<u32, ${decimationWorkgroupSize}>;
var<workgroup> nanIndices: array<u32, ${decimationWorkgroupSize}>;

fn isNan(x: f32) -> bool {
    let bits = bitcast<u32>(x);
    return (bits & 0x7f800000u) == 0x7f800000u && (bits & 0x007fffffu) != 0u;
}

@compute @workgroup_size(${decimationWorkgroupSize})
fn decimate(
    @builtin(workgroup_id) workgroupId: vec3u,
    @builtin(local_invocation_id) localId: vec3u) {
    let bucket = workgroupId.x;
    let lane = localId.x;

    if (bucket >= params.bucketCount) {
        return;
    }

    let quotient = params.visibleLength / params.bucketCount;
    let remainder = params.visibleLength % params.bucketCount;
    let startOffset = bucket * quotient + min(bucket, remainder);
    let nextBucket = bucket + 1u;
    let endOffset = nextBucket * quotient + min(nextBucket, remainder);
    let start = min(params.first + startOffset, params.sourceLength);
    let end = min(params.first + endOffset, params.sourceLength);

    var minimum = 0.0;
    var maximum = 0.0;
    var minimumIndex = 0u;
    var maximumIndex = 0u;
    var hasValue = 0u;
    var hasNan = 0u;
    var nanIndex = 0u;
    var index = start + lane;

    while (index < end) {
        let value = source[index];

        if (isNan(value)) {
            if (hasNan == 0u || index < nanIndex) {
                nanIndex = index;
            }

            hasNan = 1u;
        } else {
            if (hasValue == 0u || value < minimum || (value == minimum && index < minimumIndex)) {
                minimum = value;
                minimumIndex = index;
            }

            if (hasValue == 0u || value > maximum || (value == maximum && index < maximumIndex)) {
                maximum = value;
                maximumIndex = index;
            }

            hasValue = 1u;
        }

        index += ${decimationWorkgroupSize}u;
    }

    minimums[lane] = minimum;
    maximums[lane] = maximum;
    minimumIndices[lane] = minimumIndex;
    maximumIndices[lane] = maximumIndex;
    valid[lane] = hasValue;
    nanSeen[lane] = hasNan;
    nanIndices[lane] = nanIndex;
    workgroupBarrier();

    var stride = ${decimationWorkgroupSize / 2}u;
    while (stride > 0u) {
        if (lane < stride && valid[lane + stride] != 0u) {
            let other = lane + stride;

            if (valid[lane] == 0u || minimums[other] < minimums[lane] ||
                (minimums[other] == minimums[lane] && minimumIndices[other] < minimumIndices[lane])) {
                minimums[lane] = minimums[other];
                minimumIndices[lane] = minimumIndices[other];
            }

            if (valid[lane] == 0u || maximums[other] > maximums[lane] ||
                (maximums[other] == maximums[lane] && maximumIndices[other] < maximumIndices[lane])) {
                maximums[lane] = maximums[other];
                maximumIndices[lane] = maximumIndices[other];
            }

            valid[lane] = 1u;
        }

        if (lane < stride) {
            if (nanSeen[lane + stride] != 0u &&
                (nanSeen[lane] == 0u || nanIndices[lane + stride] < nanIndices[lane])) {
                nanIndices[lane] = nanIndices[lane + stride];
            }

            nanSeen[lane] |= nanSeen[lane + stride];
        }

        workgroupBarrier();
        stride /= 2u;
    }

    if (lane == 0u) {
        let outputIndex = bucket * 2u + 1u;

        if (bucket == 0u) {
            output[0] = vec2f(0.0, source[params.first]);
        }

        if (valid[0] == 0u || nanSeen[0] != 0u) {
            let nan = source[nanIndices[0]];
            output[outputIndex] = vec2f(f32(startOffset), nan);
            output[outputIndex + 1u] = vec2f(f32(endOffset), nan);
        } else if (minimumIndices[0] <= maximumIndices[0]) {
            output[outputIndex] = vec2f(f32(minimumIndices[0] - params.first), minimums[0]);
            output[outputIndex + 1u] = vec2f(f32(maximumIndices[0] - params.first), maximums[0]);
        } else {
            output[outputIndex] = vec2f(f32(maximumIndices[0] - params.first), maximums[0]);
            output[outputIndex + 1u] = vec2f(f32(minimumIndices[0] - params.first), minimums[0]);
        }

        if (bucket + 1u == params.bucketCount) {
            let last = params.first + params.visibleLength - 1u;
            output[outputIndex + 2u] = vec2f(f32(params.visibleLength - 1u), source[last]);
        }
    }
}
`;

    const rangeShader = `
struct Params {
    length: u32,
    workgroupCount: u32,
    _pad0: u32,
    _pad1: u32,
};

struct RangeResult {
    minimum: f32,
    maximum: f32,
    valid: u32,
    _pad: u32,
};

@group(0) @binding(0) var<storage, read> source: array<f32>;
@group(0) @binding(1) var<storage, read_write> results: array<RangeResult>;
@group(0) @binding(2) var<uniform> params: Params;

var<workgroup> minimums: array<f32, ${rangeWorkgroupSize}>;
var<workgroup> maximums: array<f32, ${rangeWorkgroupSize}>;
var<workgroup> valid: array<u32, ${rangeWorkgroupSize}>;

fn isFiniteValue(x: f32) -> bool {
    let bits = bitcast<u32>(x);
    return (bits & 0x7f800000u) != 0x7f800000u;
}

@compute @workgroup_size(${rangeWorkgroupSize})
fn reduceRange(
    @builtin(workgroup_id) workgroupId: vec3u,
    @builtin(local_invocation_id) localId: vec3u) {
    let group = workgroupId.x;
    let lane = localId.x;
    let invocationCount = params.workgroupCount * ${rangeWorkgroupSize}u;
    var index = group * ${rangeWorkgroupSize}u + lane;
    var minimum = 0.0;
    var maximum = 0.0;
    var hasValue = 0u;

    while (index < params.length) {
        let value = source[index];

        if (isFiniteValue(value)) {
            minimum = select(value, min(minimum, value), hasValue != 0u);
            maximum = select(value, max(maximum, value), hasValue != 0u);
            hasValue = 1u;
        }

        index += invocationCount;
    }

    minimums[lane] = minimum;
    maximums[lane] = maximum;
    valid[lane] = hasValue;
    workgroupBarrier();

    var stride = ${rangeWorkgroupSize / 2}u;
    while (stride > 0u) {
        if (lane < stride && valid[lane + stride] != 0u) {
            let other = lane + stride;

            if (valid[lane] == 0u) {
                minimums[lane] = minimums[other];
                maximums[lane] = maximums[other];
            } else {
                minimums[lane] = min(minimums[lane], minimums[other]);
                maximums[lane] = max(maximums[lane], maximums[other]);
            }

            valid[lane] = 1u;
        }

        workgroupBarrier();
        stride /= 2u;
    }

    if (lane == 0u) {
        results[group].minimum = minimums[0];
        results[group].maximum = maximums[0];
        results[group].valid = valid[0];
    }
}
`;

    function valueOf(source, name) {
        if (!source)
            return undefined;

        const camelName = name.charAt(0).toLowerCase() + name.slice(1);
        return source[name] ?? source[camelName];
    }

    function colorOf(source) {
        const color = valueOf(source, 'Color') ?? {};
        return [
            (valueOf(color, 'Red') ?? 0) / 255,
            (valueOf(color, 'Green') ?? 0) / 255,
            (valueOf(color, 'Blue') ?? 0) / 255,
            1,
        ];
    }

    // Bytes arrive already encoded as 32-bit floats (see Chart.razor.cs
    // ToBytes), so this is a plain reinterpretation of the buffer - a
    // zero-copy view when 4-byte aligned (the common case), and a single
    // native (non-scripted) copy otherwise. No manual per-element
    // conversion loop is needed here anymore.
    function toFloat32Array(bytes) {
        if (!bytes)
            return new Float32Array(0);

        if (bytes instanceof Uint8Array) {
            if (bytes.byteOffset % 4 === 0)
                return new Float32Array(bytes.buffer, bytes.byteOffset, bytes.byteLength / 4);

            return new Float32Array(bytes.slice().buffer);
        }

        return new Float32Array(bytes);
    }

    function ensureCanvasSize(canvas) {
        const dpr = window.devicePixelRatio || 1;
        const width = Math.max(1, Math.round(canvas.clientWidth * dpr));
        const height = Math.max(1, Math.round(canvas.clientHeight * dpr));

        if (canvas.width !== width || canvas.height !== height) {
            canvas.width = width;
            canvas.height = height;
        }

        return { width, height, dpr };
    }

    async function getInstance(chartId) {
        let instance = instances.get(chartId);

        if (instance)
            return instance;

        let pending = pendingInstances.get(chartId);

        if (pending)
            return pending;

        const promise = (async () => {
            const startedAt = performance.now();
            const canvas = document.getElementById(`series_${chartId}`);

            if (!canvas || !navigator.gpu) {
                pendingInstances.delete(chartId);
                return null;
            }

            const adapterStartedAt = performance.now();
            const adapter = await navigator.gpu.requestAdapter();
            console.log(`[chart-perf] WebGPU adapter: ${(performance.now() - adapterStartedAt).toFixed(1)} ms`);

            if (!adapter) {
                pendingInstances.delete(chartId);
                return null;
            }

            const deviceStartedAt = performance.now();
            const device = await adapter.requestDevice();
            console.log(`[chart-perf] WebGPU device: ${(performance.now() - deviceStartedAt).toFixed(1)} ms`);

            // If dispose was called while we were waiting, abort and clean up.
            if (!pendingInstances.has(chartId)) {
                device.destroy();
                return null;
            }

            const format = navigator.gpu.getPreferredCanvasFormat();
            const module = device.createShaderModule({ code: shader });
            const decimationModule = device.createShaderModule({ code: decimationShader });
            const rangeModule = device.createShaderModule({ code: rangeShader });
            const pipelinesStartedAt = performance.now();
            const pipeline = device.createRenderPipeline({
                layout: 'auto',
                vertex: {
                    module,
                    entryPoint: 'vertexMain',
                },
                fragment: {
                    module,
                    entryPoint: 'fragmentMain',
                    targets: [{
                        format,
                        blend: {
                            color: {
                                srcFactor: 'src-alpha',
                                dstFactor: 'one-minus-src-alpha',
                                operation: 'add',
                            },
                            alpha: {
                                srcFactor: 'one',
                                dstFactor: 'one-minus-src-alpha',
                                operation: 'add',
                            },
                        },
                    }],
                },
                primitive: { topology: 'triangle-list' },
            });
            const decimationPipeline = device.createComputePipeline({
                layout: 'auto',
                compute: {
                    module: decimationModule,
                    entryPoint: 'decimate',
                },
            });
            const rangePipeline = device.createComputePipeline({
                layout: 'auto',
                compute: {
                    module: rangeModule,
                    entryPoint: 'reduceRange',
                },
            });
            console.log(`[chart-perf] WebGPU pipelines: ${(performance.now() - pipelinesStartedAt).toFixed(1)} ms`);
            instance = {
                canvas,
                device,
                format,
                pipeline,
                decimationPipeline,
                rangePipeline,
                seriesBuffers: new Map(),
                targetResources: new Map(),
                previewRenderKeys: new Map(),
                uploadGenerations: new Map(),
                renderIndex: 0,
            };
            instances.set(chartId, instance);
            pendingInstances.delete(chartId);
            console.log(`[chart-perf] WebGPU initialization total: ${(performance.now() - startedAt).toFixed(1)} ms`);
            return instance;
        })();

        pendingInstances.set(chartId, promise);
        return promise;
    }

    function getSeriesKey(id, version, length) {
        return `${id}:${version}:${length}`;
    }

    function destroySeriesBuffer(cached) {
        cached.buffer.destroy();

        for (const decimation of cached.decimations?.values() ?? []) {
            decimation.outputBuffer.destroy();
            decimation.paramsBuffer.destroy();
        }
    }

    function cacheSeriesBuffer(instance, id, version, length, valuesBytes) {
        const startedAt = performance.now();
        const key = getSeriesKey(id, version, length);
        const data = toFloat32Array(valuesBytes);

        if (data.length < 2)
            return null;

        const buffer = instance.device.createBuffer({
            size: data.byteLength,
            usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
        });

        const writeStartedAt = performance.now();
        instance.device.queue.writeBuffer(buffer, 0, data);
        const writeMilliseconds = performance.now() - writeStartedAt;

        for (const [existingKey, existing] of instance.seriesBuffers) {
            if (existing.id === id && existingKey !== key) {
                destroySeriesBuffer(existing);
                instance.seriesBuffers.delete(existingKey);
            }
        }

        const cachedSeries = { id, buffer, pointBuffer: buffer, length: data.length, dataMode: 0, decimations: new Map() };
        instance.seriesBuffers.set(key, cachedSeries);
        console.log(`[chart-perf] series ${id}: GPU buffer+write submitted ${(performance.now() - startedAt).toFixed(1)} ms (writeBuffer ${writeMilliseconds.toFixed(1)} ms), ${(data.byteLength / 1048576).toFixed(1)} MiB`);
        instance.device.queue.onSubmittedWorkDone().then(() =>
            console.log(`[chart-perf] series ${id}: GPU upload queue settled ${(performance.now() - startedAt).toFixed(1)} ms`));
        return cachedSeries;
    }

    function getSeriesBuffer(instance, series) {
        const id = valueOf(series, 'Id');
        const version = valueOf(series, 'DataVersion') ?? 0;
        const length = valueOf(series, 'Length') ?? 0;

        if (length < 2)
            return null;

        return instance.seriesBuffers.get(getSeriesKey(id, version, length)) ?? null;
    }

    function getPreviewRenderKey(instance, payload, width, height) {
        const zoom = valueOf(payload, 'Zoom') ?? {};
        const seriesList = valueOf(payload, 'Series') ?? [];
        const seriesKey = seriesList.map(series => {
            const id = valueOf(series, 'Id');
            const version = valueOf(series, 'DataVersion') ?? 0;
            const length = valueOf(series, 'Length') ?? 0;
            const color = valueOf(series, 'Color') ?? {};

            return [
                id,
                version,
                length,
                instance.seriesBuffers.has(getSeriesKey(id, version, length)),
                valueOf(series, 'OverviewAxisMin'),
                valueOf(series, 'OverviewAxisMax'),
                valueOf(color, 'Red'),
                valueOf(color, 'Green'),
                valueOf(color, 'Blue'),
                valueOf(color, 'Alpha'),
            ];
        });

        return JSON.stringify([
            width,
            height,
            valueOf(zoom, 'Left') ?? 0,
            valueOf(zoom, 'Right') ?? 1,
            valueOf(payload, 'LineWidth') ?? 0.7,
            valueOf(payload, 'FillOpacity') ?? 0.10,
            seriesKey,
        ]);
    }

    async function calculateSeriesRangeAsync(instance, source, length) {
        const startedAt = performance.now();
        const workgroupCount = Math.min(maxRangeWorkgroups, Math.max(1, Math.ceil(length / rangeWorkgroupSize)));
        const resultSize = workgroupCount * 16;
        const resultBuffer = instance.device.createBuffer({ size: resultSize, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC });
        const readbackBuffer = instance.device.createBuffer({ size: resultSize, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
        const paramsBuffer = instance.device.createBuffer({ size: 16, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });

        try {
            instance.device.queue.writeBuffer(paramsBuffer, 0, new Uint32Array([length, workgroupCount, 0, 0]));
            const bindGroup = instance.device.createBindGroup({
                layout: instance.rangePipeline.getBindGroupLayout(0),
                entries: [
                    { binding: 0, resource: { buffer: source } },
                    { binding: 1, resource: { buffer: resultBuffer } },
                    { binding: 2, resource: { buffer: paramsBuffer } },
                ],
            });
            const encoder = instance.device.createCommandEncoder();
            const pass = encoder.beginComputePass();
            pass.setPipeline(instance.rangePipeline);
            pass.setBindGroup(0, bindGroup);
            pass.dispatchWorkgroups(workgroupCount);
            pass.end();
            encoder.copyBufferToBuffer(resultBuffer, 0, readbackBuffer, 0, resultSize);
            instance.device.queue.submit([encoder.finish()]);
            await readbackBuffer.mapAsync(GPUMapMode.READ);

            const view = new DataView(readbackBuffer.getMappedRange());
            let minimum = 0;
            let maximum = 0;
            let hasValue = false;

            for (let i = 0; i < workgroupCount; i++) {
                const offset = i * 16;

                if (view.getUint32(offset + 8, true) === 0)
                    continue;

                const localMinimum = view.getFloat32(offset, true);
                const localMaximum = view.getFloat32(offset + 4, true);

                if (!Number.isFinite(localMinimum) || !Number.isFinite(localMaximum))
                    throw new Error(`WebGPU range reduction returned non-finite values for workgroup ${i}`);

                minimum = hasValue ? Math.min(minimum, localMinimum) : localMinimum;
                maximum = hasValue ? Math.max(maximum, localMaximum) : localMaximum;
                hasValue = true;
            }

            console.log(`[chart-perf] GPU range: ${(performance.now() - startedAt).toFixed(1)} ms, ${length.toLocaleString()} points, ${workgroupCount} workgroups`);
            return { hasValue, minimum, maximum };
        } finally {
            if (readbackBuffer.mapState === 'mapped')
                readbackBuffer.unmap();

            resultBuffer.destroy();
            readbackBuffer.destroy();
            paramsBuffer.destroy();
        }
    }

    function getDrawResources(instance, seriesBuffer, drawIndex, target) {
        let targetResources = instance.targetResources.get(target);

        if (!targetResources) {
            targetResources = [];
            instance.targetResources.set(target, targetResources);
        }

        let resources = targetResources[drawIndex];

        if (resources?.seriesBuffer === seriesBuffer)
            return resources;

        resources?.uniformBuffer.destroy();

        const uniformBuffer = instance.device.createBuffer({
            size: uniformBufferSize,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
        });
        const bindGroup = instance.device.createBindGroup({
            layout: instance.pipeline.getBindGroupLayout(0),
            entries: [
                { binding: 0, resource: { buffer: uniformBuffer } },
                { binding: 1, resource: { buffer: seriesBuffer.buffer } },
                { binding: 2, resource: { buffer: seriesBuffer.pointBuffer } },
            ],
        });

        resources = { seriesBuffer, uniformBuffer, bindGroup };
        targetResources[drawIndex] = resources;
        return resources;
    }

    function getPlot(payload, width, height) {
        const plot = valueOf(payload, 'Plot') ?? {};
        const plotLeft = (valueOf(plot, 'Left') ?? 0) * width;
        const plotTop = (valueOf(plot, 'Top') ?? 0) * height;
        const plotRight = (valueOf(plot, 'Right') ?? 1) * width;
        const plotBottom = (valueOf(plot, 'Bottom') ?? 1) * height;
        const plotWidth = plotRight - plotLeft;
        const plotHeight = plotBottom - plotTop;

        if (plotWidth <= 0 || plotHeight <= 0)
            return null;

        return { plotLeft, plotTop, plotRight, plotBottom, plotWidth, plotHeight };
    }

    function getZoomInfo(payload, length, plot) {
        const zoom = valueOf(payload, 'Zoom') ?? {};
        const indexLeft = (valueOf(zoom, 'Left') ?? 0) * length;
        const indexRight = (valueOf(zoom, 'Right') ?? 1) * length;
        const indexRange = indexRight - indexLeft;

        if (!Number.isFinite(indexRange) || indexRange <= 0)
            return null;

        const indexLeftRounded = Math.floor(indexLeft);
        const indexRightRounded = Math.ceil(indexRight);
        const zoomedLeft = plot.plotLeft - plot.plotWidth * ((indexLeft - indexLeftRounded) / indexRange);
        const zoomedRight = plot.plotRight + plot.plotWidth * ((indexRightRounded - indexRight) / indexRange);
        const first = Math.max(0, indexLeftRounded);
        const last = Math.min(length - 1, indexRightRounded);
        const intendedLength = (indexRightRounded + 1) - indexLeftRounded;
        const visibleLength = last - first + 1;
        const isClippedRight = visibleLength < intendedLength;
        const denominator = isClippedRight ? visibleLength : visibleLength - 1;
        const dx = (zoomedRight - zoomedLeft) / denominator;

        if (visibleLength < 2 || !Number.isFinite(dx) || dx <= 0)
            return null;

        return { first, segmentCount: visibleLength - 1, zoomedLeft, dx };
    }

    function getRenderBuffer(instance, source, zoomInfo, plot, encoder, target) {
        const visibleLength = zoomInfo.segmentCount + 1;
        const bucketCount = Math.min(maxDecimationBuckets, Math.max(2, Math.ceil(plot.plotWidth * decimationBucketsPerPixel)));

        if (visibleLength <= bucketCount * decimationFactor)
            return { seriesBuffer: source, zoomInfo };

        let decimation = source.decimations.get(target);
        const outputLength = bucketCount * 2 + 2;
        const outputSize = outputLength * 2 * Float32Array.BYTES_PER_ELEMENT;

        if (!decimation || decimation.bucketCount !== bucketCount) {
            decimation?.outputBuffer.destroy();
            decimation?.paramsBuffer.destroy();

            const outputBuffer = instance.device.createBuffer({
                size: outputSize,
                usage: GPUBufferUsage.STORAGE,
            });
            const paramsBuffer = instance.device.createBuffer({
                size: 16,
                usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
            });
            const bindGroup = instance.device.createBindGroup({
                layout: instance.decimationPipeline.getBindGroupLayout(0),
                entries: [
                    { binding: 0, resource: { buffer: source.buffer } },
                    { binding: 1, resource: { buffer: outputBuffer } },
                    { binding: 2, resource: { buffer: paramsBuffer } },
                ],
            });

            decimation = {
                bucketCount,
                outputBuffer,
                paramsBuffer,
                bindGroup,
                renderBuffer: {
                    buffer: source.buffer,
                    pointBuffer: outputBuffer,
                    length: outputLength,
                    dataMode: 1,
                },
            };
            source.decimations.set(target, decimation);
        }

        instance.device.queue.writeBuffer(
            decimation.paramsBuffer,
            0,
            new Uint32Array([zoomInfo.first, visibleLength, bucketCount, source.length]));

        const pass = encoder.beginComputePass();
        pass.setPipeline(instance.decimationPipeline);
        pass.setBindGroup(0, decimation.bindGroup);
        pass.dispatchWorkgroups(bucketCount);
        pass.end();

        return {
            seriesBuffer: decimation.renderBuffer,
            zoomInfo: {
                first: 0,
                segmentCount: outputLength - 1,
                zoomedLeft: zoomInfo.zoomedLeft,
                dx: zoomInfo.dx,
            },
        };
    }

    function writeUniforms(instance, uniformBuffer, seriesBuffer, payload, series, plot, zoomInfo, width, height, dpr, mode) {
        const preview = valueOf(payload, 'Preview') ?? false;
        const axisMin = preview ? (valueOf(series, 'OverviewAxisMin') ?? 0) : (valueOf(series, 'AxisMin') ?? 0);
        const axisMax = preview ? (valueOf(series, 'OverviewAxisMax') ?? 1) : (valueOf(series, 'AxisMax') ?? 1);
        const axisRange = axisMax - axisMin;

        if (!Number.isFinite(axisRange) || axisRange === 0)
            return false;

        const lineWidth = (valueOf(payload, 'LineWidth') ?? 0.7) * dpr;
        const fillOpacity = valueOf(payload, 'FillOpacity') ?? 0.10;
        const zeroY = Math.min(plot.plotBottom, Math.max(plot.plotTop, plot.plotBottom - (0 - axisMin) / axisRange * plot.plotHeight));
        const color = colorOf(series);
        const data = new ArrayBuffer(uniformBufferSize);
        const floats = new Float32Array(data);
        const uints = new Uint32Array(data);

        floats[0] = width;
        floats[1] = height;
        floats[4] = plot.plotLeft;
        floats[5] = plot.plotTop;
        floats[6] = plot.plotRight;
        floats[7] = plot.plotBottom;
        floats[8] = axisMin;
        floats[9] = axisRange;
        floats[10] = zoomInfo.zoomedLeft;
        floats[11] = zoomInfo.dx;
        floats[12] = zeroY;
        floats[13] = lineWidth;
        floats[14] = fillOpacity;
        floats[16] = color[0];
        floats[17] = color[1];
        floats[18] = color[2];
        floats[19] = color[3];
        uints[20] = zoomInfo.first;
        uints[21] = mode;
        uints[22] = seriesBuffer.dataMode ?? 0;

        instance.device.queue.writeBuffer(uniformBuffer, 0, data);
        return true;
    }

    async function loadSeriesDataAsync(chartId, id, version, length, streamReference) {
        const startedAt = performance.now();
        const instance = await getInstance(chartId);
        const instanceMilliseconds = performance.now() - startedAt;

        if (!instance)
            throw new Error(`WebGPU instance unavailable for chart ${chartId}`);

        const generation = (instance.uploadGenerations.get(id) ?? 0) + 1;
        instance.uploadGenerations.set(id, generation);
        const streamStartedAt = performance.now();
        const bytes = new Uint8Array(await streamReference.arrayBuffer());
        const streamMilliseconds = performance.now() - streamStartedAt;

        if (instances.get(chartId) !== instance || instance.uploadGenerations.get(id) !== generation)
            return;

        const cached = cacheSeriesBuffer(instance, id, version, length, bytes);
        const range = cached
            ? await calculateSeriesRangeAsync(instance, cached.buffer, cached.length)
            : { hasValue: false, minimum: 0, maximum: 0 };

        if (instances.get(chartId) !== instance || instance.uploadGenerations.get(id) !== generation)
            return { hasValue: false, minimum: 0, maximum: 0 };

        console.log(`[chart-perf] series ${id}: getInstance ${instanceMilliseconds.toFixed(1)} ms; stream ${(streamMilliseconds).toFixed(1)} ms; JS load total ${(performance.now() - startedAt).toFixed(1)} ms`);
        return range;
    }

    async function renderSeriesAsync(chartId, payload) {
        const startedAt = performance.now();
        const instance = await getInstance(chartId);

        if (!instance)
            return;

        const { device, format, pipeline } = instance;
        const target = valueOf(payload, 'Target') ?? 'series';
        const canvas = document.getElementById(`${target}_${chartId}`);

        if (!canvas)
            return;

        const context = canvas.getContext('webgpu');
        const renderIndex = ++instance.renderIndex;
        const { width, height, dpr } = ensureCanvasSize(canvas);
        const isPreview = valueOf(payload, 'Preview') ?? false;
        let previewRenderKey = null;

        if (isPreview) {
            previewRenderKey = getPreviewRenderKey(instance, payload, width, height);

            if (instance.previewRenderKeys.get(target) === previewRenderKey)
                return;
        }

        const plot = getPlot(payload, width, height);

        context.configure({
            device,
            format,
            alphaMode: 'premultiplied',
        });

        const encoder = device.createCommandEncoder();
        const renderItems = [];

        if (plot) {
            const seriesList = valueOf(payload, 'Series') ?? [];

            for (const series of seriesList) {
                const cached = getSeriesBuffer(instance, series);

                if (!cached)
                    continue;

                const zoomInfo = getZoomInfo(payload, cached.length, plot);

                if (!zoomInfo)
                    continue;

                const renderItem = getRenderBuffer(instance, cached, zoomInfo, plot, encoder, target);
                renderItems.push({ series, ...renderItem });
            }
        }

        const pass = encoder.beginRenderPass({
            colorAttachments: [{
                view: context.getCurrentTexture().createView(),
                loadOp: 'clear',
                storeOp: 'store',
                clearValue: { r: 0, g: 0, b: 0, a: 0 },
            }],
        });

        if (plot) {
            pass.setPipeline(pipeline);
            pass.setScissorRect(
                Math.max(0, Math.floor(plot.plotLeft)),
                Math.max(0, Math.floor(plot.plotTop)),
                Math.max(1, Math.ceil(plot.plotWidth)),
                Math.max(1, Math.ceil(plot.plotHeight)));

            let drawIndex = 0;

            for (const { series, seriesBuffer, zoomInfo } of renderItems) {
                const fillResources = getDrawResources(instance, seriesBuffer, drawIndex++, target);

                if (writeUniforms(instance, fillResources.uniformBuffer, seriesBuffer, payload, series, plot, zoomInfo, width, height, dpr, 0)) {
                    pass.setBindGroup(0, fillResources.bindGroup);
                    pass.draw(zoomInfo.segmentCount * fillVerticesPerSegment);
                }

                const lineResources = getDrawResources(instance, seriesBuffer, drawIndex++, target);

                if (writeUniforms(instance, lineResources.uniformBuffer, seriesBuffer, payload, series, plot, zoomInfo, width, height, dpr, 1)) {
                    pass.setBindGroup(0, lineResources.bindGroup);
                    pass.draw(zoomInfo.segmentCount * lineVerticesPerSegment);
                }
            }
        }

        pass.end();
        const submitStartedAt = performance.now();
        device.queue.submit([encoder.finish()]);

        if (previewRenderKey !== null)
            instance.previewRenderKeys.set(target, previewRenderKey);

        const submittedAt = performance.now();
        console.log(`[chart-perf] render ${renderIndex}: encoded+submitted ${(submittedAt - startedAt).toFixed(1)} ms; submit ${(submittedAt - submitStartedAt).toFixed(1)} ms; ${renderItems.length} series`);
        device.queue.onSubmittedWorkDone().then(() =>
            console.log(`[chart-perf] render ${renderIndex}: GPU queue settled ${(performance.now() - submittedAt).toFixed(1)} ms after submit, ${(performance.now() - startedAt).toFixed(1)} ms total`));
    }

    window.nexus ??= {};
    window.nexus.chartWebGpu = {
        loadSeriesData(chartId, id, version, length, streamReference) {
            return loadSeriesDataAsync(chartId, id, version, length, streamReference);
        },
        renderSeries(chartId, payload) {
            renderSeriesAsync(chartId, payload).catch(error => console.error('[chart-webgpu] render failed', error));
        },
        dispose(chartId) {
            pendingInstances.delete(chartId);

            const instance = instances.get(chartId);

            if (instance) {
                for (const cached of instance.seriesBuffers.values())
                    destroySeriesBuffer(cached);

                for (const targetResources of instance.targetResources.values()) {
                    for (const resources of targetResources)
                        resources.uniformBuffer.destroy();
                }
            }

            instances.delete(chartId);
        },
    };
})();
