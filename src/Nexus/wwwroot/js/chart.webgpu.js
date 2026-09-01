(function () {
    const instances = new Map();
    const pendingInstances = new Map();
    const uniformBufferSize = 96;
    const fillVerticesPerSegment = 6;
    const lineVerticesPerSegment = 18;
    const decimationWorkgroupSize = 64;
    const decimationFactor = 4;
    const maxDecimationBuckets = 8192;

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
            const canvas = document.getElementById(`series_${chartId}`);

            if (!canvas || !navigator.gpu) {
                pendingInstances.delete(chartId);
                return null;
            }

            const adapter = await navigator.gpu.requestAdapter();

            if (!adapter) {
                pendingInstances.delete(chartId);
                return null;
            }

            const device = await adapter.requestDevice();

            // If dispose was called while we were waiting, abort and clean up.
            if (!pendingInstances.has(chartId)) {
                device.destroy();
                return null;
            }

            const context = canvas.getContext('webgpu');
            const format = navigator.gpu.getPreferredCanvasFormat();
            const module = device.createShaderModule({ code: shader });
            const decimationModule = device.createShaderModule({ code: decimationShader });
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
            instance = {
                canvas,
                context,
                device,
                format,
                pipeline,
                decimationPipeline,
                seriesBuffers: new Map(),
                drawResources: [],
                uploadGenerations: new Map(),
            };
            instances.set(chartId, instance);
            pendingInstances.delete(chartId);
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
        cached.decimation?.outputBuffer.destroy();
        cached.decimation?.paramsBuffer.destroy();
    }

    function cacheSeriesBuffer(instance, id, version, length, valuesBytes) {
        const key = getSeriesKey(id, version, length);
        const data = toFloat32Array(valuesBytes);

        if (data.length < 2)
            return null;

        const buffer = instance.device.createBuffer({
            size: data.byteLength,
            usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
        });

        instance.device.queue.writeBuffer(buffer, 0, data);

        for (const [existingKey, existing] of instance.seriesBuffers) {
            if (existing.id === id && existingKey !== key) {
                destroySeriesBuffer(existing);
                instance.seriesBuffers.delete(existingKey);
            }
        }

        const cachedSeries = { id, buffer, pointBuffer: buffer, length: data.length, dataMode: 0 };
        instance.seriesBuffers.set(key, cachedSeries);
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

    function getDrawResources(instance, seriesBuffer, drawIndex) {
        let resources = instance.drawResources[drawIndex];

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
        instance.drawResources[drawIndex] = resources;
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

    function getRenderBuffer(instance, source, zoomInfo, plot, encoder) {
        const visibleLength = zoomInfo.segmentCount + 1;
        const bucketCount = Math.min(maxDecimationBuckets, Math.max(2, Math.ceil(plot.plotWidth)));

        if (visibleLength <= bucketCount * decimationFactor)
            return { seriesBuffer: source, zoomInfo };

        let decimation = source.decimation;
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
            source.decimation = decimation;
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
        const axisMin = valueOf(series, 'AxisMin') ?? 0;
        const axisMax = valueOf(series, 'AxisMax') ?? 1;
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
        const instance = await getInstance(chartId);

        if (!instance)
            throw new Error(`WebGPU instance unavailable for chart ${chartId}`);

        const generation = (instance.uploadGenerations.get(id) ?? 0) + 1;
        instance.uploadGenerations.set(id, generation);
        const bytes = new Uint8Array(await streamReference.arrayBuffer());

        if (instances.get(chartId) !== instance || instance.uploadGenerations.get(id) !== generation)
            return;

        cacheSeriesBuffer(instance, id, version, length, bytes);
    }

    async function renderSeriesAsync(chartId, payload) {
        const instance = await getInstance(chartId);

        if (!instance)
            return;

        const { canvas, context, device, format, pipeline } = instance;
        const { width, height, dpr } = ensureCanvasSize(canvas);
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

                const renderItem = getRenderBuffer(instance, cached, zoomInfo, plot, encoder);
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
                const fillResources = getDrawResources(instance, seriesBuffer, drawIndex++);

                if (writeUniforms(instance, fillResources.uniformBuffer, seriesBuffer, payload, series, plot, zoomInfo, width, height, dpr, 0)) {
                    pass.setBindGroup(0, fillResources.bindGroup);
                    pass.draw(zoomInfo.segmentCount * fillVerticesPerSegment);
                }

                const lineResources = getDrawResources(instance, seriesBuffer, drawIndex++);

                if (writeUniforms(instance, lineResources.uniformBuffer, seriesBuffer, payload, series, plot, zoomInfo, width, height, dpr, 1)) {
                    pass.setBindGroup(0, lineResources.bindGroup);
                    pass.draw(zoomInfo.segmentCount * lineVerticesPerSegment);
                }
            }
        }

        pass.end();
        device.queue.submit([encoder.finish()]);
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

                for (const resources of instance.drawResources)
                    resources.uniformBuffer.destroy();
            }

            instances.delete(chartId);
        },
    };
})();
