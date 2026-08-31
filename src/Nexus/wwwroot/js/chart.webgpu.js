(function () {
    const instances = new Map();

    const shader = `
struct VertexOut {
    @builtin(position) position: vec4f,
    @location(0) color: vec4f,
};

@vertex
fn vertexMain(
    @location(0) position: vec2f,
    @location(1) color: vec4f
) -> VertexOut {
    var out: VertexOut;
    out.position = vec4f(position, 0.0, 1.0);
    out.color = color;
    return out;
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

    function colorOf(source, alpha) {
        const color = valueOf(source, 'Color') ?? {};
        return [
            (valueOf(color, 'Red') ?? 0) / 255,
            (valueOf(color, 'Green') ?? 0) / 255,
            (valueOf(color, 'Blue') ?? 0) / 255,
            alpha,
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
                buffers: [{
                    arrayStride: 24,
                    attributes: [
                        { shaderLocation: 0, offset: 0, format: 'float32x2' },
                        { shaderLocation: 1, offset: 8, format: 'float32x4' },
                    ],
                }],
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

        instance = { canvas, context, device, format, pipeline };
        instances.set(chartId, instance);
        return instance;
    }

    function toNdcX(x, width) {
        return x / width * 2 - 1;
    }

    function toNdcY(y, height) {
        return 1 - y / height * 2;
    }

    function appendVertex(vertices, x, y, color, width, height) {
        vertices.push(toNdcX(x, width), toNdcY(y, height), color[0], color[1], color[2], color[3]);
    }

    function appendTriangle(vertices, a, b, c, color, width, height) {
        appendVertex(vertices, a.x, a.y, color, width, height);
        appendVertex(vertices, b.x, b.y, color, width, height);
        appendVertex(vertices, c.x, c.y, color, width, height);
    }

    function appendTriangleColors(vertices, a, aColor, b, bColor, c, cColor, width, height) {
        appendVertex(vertices, a.x, a.y, aColor, width, height);
        appendVertex(vertices, b.x, b.y, bColor, width, height);
        appendVertex(vertices, c.x, c.y, cColor, width, height);
    }

    function appendLineQuad(vertices, a, b, color, lineWidth, width, height) {
        const dx = b.x - a.x;
        const dy = b.y - a.y;
        const length = Math.hypot(dx, dy);

        if (!Number.isFinite(length) || length <= 0)
            return;

        const half = lineWidth / 2;
        const fringe = 0.5;
        const ux = -dy / length;
        const uy = dx / length;
        const nx = ux * half;
        const ny = uy * half;
        const ox = ux * (half + fringe);
        const oy = uy * (half + fringe);

        const p0 = { x: a.x + nx, y: a.y + ny };
        const p1 = { x: a.x - nx, y: a.y - ny };
        const p2 = { x: b.x + nx, y: b.y + ny };
        const p3 = { x: b.x - nx, y: b.y - ny };
        const o0 = { x: a.x + ox, y: a.y + oy };
        const o1 = { x: a.x - ox, y: a.y - oy };
        const o2 = { x: b.x + ox, y: b.y + oy };
        const o3 = { x: b.x - ox, y: b.y - oy };
        const transparent = [color[0], color[1], color[2], 0];

        appendTriangle(vertices, p0, p1, p2, color, width, height);
        appendTriangle(vertices, p2, p1, p3, color, width, height);
        appendTriangleColors(vertices, o0, transparent, p0, color, o2, transparent, width, height);
        appendTriangleColors(vertices, o2, transparent, p0, color, p2, color, width, height);
        appendTriangleColors(vertices, p1, color, o1, transparent, p3, color, width, height);
        appendTriangleColors(vertices, p3, color, o1, transparent, o3, transparent, width, height);
    }

    function interpolateZero(a, b) {
        const delta = b.value - a.value;

        if (delta === 0)
            return null;

        const t = -a.value / delta;

        if (t <= 0 || t >= 1)
            return null;

        return {
            x: a.x + (b.x - a.x) * t,
            y: a.zeroY,
            value: 0,
            zeroY: a.zeroY,
        };
    }

    function appendArea(vertices, a, b, fillColor, width, height) {
        const zero = interpolateZero(a, b);

        if (zero) {
            appendArea(vertices, a, zero, fillColor, width, height);
            appendArea(vertices, zero, b, fillColor, width, height);
            return;
        }

        const a0 = { x: a.x, y: a.zeroY };
        const b0 = { x: b.x, y: b.zeroY };

        appendTriangle(vertices, a0, a, b, fillColor, width, height);
        appendTriangle(vertices, a0, b, b0, fillColor, width, height);
    }

    function buildVertices(payload, width, height, dpr) {
        const plot = valueOf(payload, 'Plot') ?? {};
        const zoom = valueOf(payload, 'Zoom') ?? {};
        const seriesList = valueOf(payload, 'Series') ?? [];
        const lineWidth = (valueOf(payload, 'LineWidth') ?? 0.7) * dpr;
        const fillOpacity = valueOf(payload, 'FillOpacity') ?? 0.10;
        const plotLeft = (valueOf(plot, 'Left') ?? 0) * width;
        const plotTop = (valueOf(plot, 'Top') ?? 0) * height;
        const plotRight = (valueOf(plot, 'Right') ?? 1) * width;
        const plotBottom = (valueOf(plot, 'Bottom') ?? 1) * height;
        const plotWidth = plotRight - plotLeft;
        const plotHeight = plotBottom - plotTop;
        const vertices = [];

        if (plotWidth <= 0 || plotHeight <= 0)
            return { vertices, scissor: null };

        for (const series of seriesList) {
            const y = createYVector(valueOf(series, 'ValuesBytes'));

            if (y.length < 2)
                continue;

            const axisMin = valueOf(series, 'AxisMin') ?? 0;
            const axisMax = valueOf(series, 'AxisMax') ?? 1;
            const axisRange = axisMax - axisMin;

            if (!Number.isFinite(axisRange) || axisRange === 0)
                continue;

            const indexLeft = (valueOf(zoom, 'Left') ?? 0) * y.length;
            const indexRight = (valueOf(zoom, 'Right') ?? 1) * y.length;
            const indexRange = indexRight - indexLeft;

            if (!Number.isFinite(indexRange) || indexRange <= 0)
                continue;

            const strokeColor = colorOf(series, 1);
            const fillColor = colorOf(series, fillOpacity);
            const zeroY = Math.min(plotBottom, Math.max(plotTop, plotBottom - (0 - axisMin) / axisRange * plotHeight));
            const indexLeftRounded = Math.floor(indexLeft);
            const indexRightRounded = Math.ceil(indexRight);
            const zoomedLeft = plotLeft - plotWidth * ((indexLeft - indexLeftRounded) / indexRange);
            const zoomedRight = plotRight + plotWidth * ((indexRightRounded - indexRight) / indexRange);
            const first = Math.max(0, indexLeftRounded);
            const last = Math.min(y.length - 1, indexRightRounded);
            const intendedLength = (indexRightRounded + 1) - indexLeftRounded;
            const visibleLength = last - first + 1;
            const isClippedRight = visibleLength < intendedLength;
            const dx = (zoomedRight - zoomedLeft) / (isClippedRight ? visibleLength : visibleLength - 1);
            let previous = null;

            if (!Number.isFinite(dx) || dx <= 0)
                continue;

            for (let i = first; i <= last; i++) {
                const value = y[i];

                if (!Number.isFinite(value)) {
                    previous = null;
                    continue;
                }

                const point = {
                    x: zoomedLeft + dx * (i - first),
                    y: plotBottom - (value - axisMin) / axisRange * plotHeight,
                    value,
                    zeroY,
                };

                if (previous) {
                    appendArea(vertices, previous, point, fillColor, width, height);
                    appendLineQuad(vertices, previous, point, strokeColor, lineWidth, width, height);
                }

                previous = point;
            }
        }

        return {
            vertices,
            scissor: {
                x: Math.max(0, Math.floor(plotLeft)),
                y: Math.max(0, Math.floor(plotTop)),
                width: Math.max(1, Math.ceil(plotWidth)),
                height: Math.max(1, Math.ceil(plotHeight)),
            },
        };
    }

    async function renderSeriesAsync(chartId, payload) {
        const instance = await getInstance(chartId);

        if (!instance)
            return;

        const { canvas, context, device, format, pipeline } = instance;
        const { width, height, dpr } = ensureCanvasSize(canvas);

        context.configure({
            device,
            format,
            alphaMode: 'premultiplied',
        });

        const { vertices, scissor } = buildVertices(payload, width, height, dpr);
        const encoder = device.createCommandEncoder();
        const pass = encoder.beginRenderPass({
            colorAttachments: [{
                view: context.getCurrentTexture().createView(),
                loadOp: 'clear',
                storeOp: 'store',
                clearValue: { r: 0, g: 0, b: 0, a: 0 },
            }],
        });

        if (vertices.length > 0 && scissor) {
            const vertexData = new Float32Array(vertices);
            const buffer = device.createBuffer({
                size: vertexData.byteLength,
                usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
            });

            device.queue.writeBuffer(buffer, 0, vertexData);
            pass.setPipeline(pipeline);
            pass.setVertexBuffer(0, buffer);
            pass.setScissorRect(scissor.x, scissor.y, scissor.width, scissor.height);
            pass.draw(vertexData.length / 6);
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
            instances.delete(chartId);
        },
    };
})();
