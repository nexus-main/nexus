(function () {
    const instances = new Map();
    const uniformBufferSize = 96;
    const fillVerticesPerSegment = 6;
    const lineVerticesPerSegment = 18;

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
    _pad2: vec2<u32>,
};

struct VertexOut {
    @builtin(position) position: vec4f,
    @location(0) color: vec4f,
};

@group(0) @binding(0) var<uniform> uniforms: Uniforms;
@group(0) @binding(1) var<storage, read> values: array<f32>;

fn toNdc(point: vec2f) -> vec4f {
    return vec4f(point.x / uniforms.viewport.x * 2.0 - 1.0, 1.0 - point.y / uniforms.viewport.y * 2.0, 0.0, 1.0);
}

fn dataPoint(index: u32) -> vec2f {
    let value = values[index];
    return vec2f(
        uniforms.xParams.x + uniforms.xParams.y * f32(index - uniforms.startIndex),
        uniforms.plot.w - ((value - uniforms.axis.x) / uniforms.axis.y) * (uniforms.plot.w - uniforms.plot.y));
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
    let valueA = values[index];
    let valueB = values[index + 1u];

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

    function createYVector(values) {
        if (!values)
            return new Float64Array(0);

        if (values instanceof Uint8Array) {
            if (values.byteOffset % 8 === 0)
                return new Float64Array(values.buffer, values.byteOffset, values.byteLength / 8);

            return new Float64Array(values.slice().buffer);
        }

        return new Float64Array(values);
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

        const canvas = document.getElementById(`series_${chartId}`);

        if (!canvas || !navigator.gpu)
            return null;

        const adapter = await navigator.gpu.requestAdapter();

        if (!adapter)
            return null;

        const device = await adapter.requestDevice();
        const context = canvas.getContext('webgpu');
        const format = navigator.gpu.getPreferredCanvasFormat();
        const module = device.createShaderModule({ code: shader });
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
        instance = { canvas, context, device, format, pipeline, seriesBuffers: new Map(), drawResources: [] };
        instances.set(chartId, instance);
        return instance;
    }

    function getSeriesKey(series, y) {
        return JSON.stringify({
            id: valueOf(series, 'Id'),
            version: valueOf(series, 'DataVersion') ?? 0,
            length: y.length,
        });
    }

    function getSeriesBuffer(instance, series) {
        const y = createYVector(valueOf(series, 'ValuesBytes'));

        if (y.length < 2)
            return null;

        const key = getSeriesKey(series, y);
        const cached = instance.seriesBuffers.get(key);

        if (cached)
            return cached;

        const data = new Float32Array(y.length);

        for (let i = 0; i < y.length; i++)
            data[i] = y[i];

        const buffer = instance.device.createBuffer({
            size: data.byteLength,
            usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
        });

        instance.device.queue.writeBuffer(buffer, 0, data);

        const id = valueOf(series, 'Id');

        for (const [existingKey, existing] of instance.seriesBuffers) {
            if (existing.id === id && existingKey !== key) {
                existing.buffer.destroy();
                instance.seriesBuffers.delete(existingKey);
            }
        }

        const cachedSeries = { id, buffer, length: data.length };
        instance.seriesBuffers.set(key, cachedSeries);
        return cachedSeries;
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

    function writeUniforms(instance, uniformBuffer, payload, series, plot, zoomInfo, width, height, dpr, mode) {
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

        instance.device.queue.writeBuffer(uniformBuffer, 0, data);
        return true;
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
        const pass = encoder.beginRenderPass({
            colorAttachments: [{
                view: context.getCurrentTexture().createView(),
                loadOp: 'clear',
                storeOp: 'store',
                clearValue: { r: 0, g: 0, b: 0, a: 0 },
            }],
        });

        if (plot) {
            const seriesList = valueOf(payload, 'Series') ?? [];

            pass.setPipeline(pipeline);
            pass.setScissorRect(
                Math.max(0, Math.floor(plot.plotLeft)),
                Math.max(0, Math.floor(plot.plotTop)),
                Math.max(1, Math.ceil(plot.plotWidth)),
                Math.max(1, Math.ceil(plot.plotHeight)));

            let drawIndex = 0;

            for (const series of seriesList) {
                const cached = getSeriesBuffer(instance, series);

                if (!cached)
                    continue;

                const zoomInfo = getZoomInfo(payload, cached.length, plot);

                if (!zoomInfo)
                    continue;

                const fillResources = getDrawResources(instance, cached, drawIndex++);

                if (writeUniforms(instance, fillResources.uniformBuffer, payload, series, plot, zoomInfo, width, height, dpr, 0)) {
                    pass.setBindGroup(0, fillResources.bindGroup);
                    pass.draw(zoomInfo.segmentCount * fillVerticesPerSegment);
                }

                const lineResources = getDrawResources(instance, cached, drawIndex++);

                if (writeUniforms(instance, lineResources.uniformBuffer, payload, series, plot, zoomInfo, width, height, dpr, 1)) {
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
        renderSeries(chartId, payload) {
            renderSeriesAsync(chartId, payload).catch(error => console.error('[chart-webgpu] render failed', error));
        },
        dispose(chartId) {
            const instance = instances.get(chartId);

            if (instance) {
                for (const cached of instance.seriesBuffers.values())
                    cached.buffer.destroy();

                for (const resources of instance.drawResources)
                    resources.uniformBuffer.destroy();
            }

            instances.delete(chartId);
        },
    };
})();
