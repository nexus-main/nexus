(function () {
    'use strict';

    const state = {};

    const SHADER_CODE = `
struct Uniforms {
    data_box: vec4f,
    resolution: vec2f,
    index_offset: f32,
    index_scale: f32,
    axis_min: f32,
    axis_max: f32,
    zero_level: f32,
    data_offset: f32,
    stroke_color: vec4f,
    fill_color: vec4f,
};

@group(0) @binding(0) var<uniform> uniforms: Uniforms;
@group(0) @binding(1) var<storage, read> data: array<f32>;

struct VOut {
    @builtin(position) position: vec4f,
    @location(0) @interpolate(flat) color: vec4f,
};

fn sampleX(i: f32) -> f32 {
    return uniforms.data_box.x
        + ((i - uniforms.index_offset) / uniforms.index_scale)
        * (uniforms.data_box.z - uniforms.data_box.x);
}

fn sampleY(value: f32) -> f32 {
    return uniforms.data_box.w
        - (value - uniforms.axis_min) / (uniforms.axis_max - uniforms.axis_min)
        * (uniforms.data_box.w - uniforms.data_box.y);
}

fn mapToClipSpace(x: f32, y: f32) -> vec4f {
    let clip_x = 2.0 * x / uniforms.resolution.x - 1.0;
    let clip_y = 1.0 - 2.0 * y / uniforms.resolution.y;
    return vec4f(clip_x, clip_y, 0.0, 1.0);
}

@vertex
fn vs_stroke(@builtin(vertex_index) vi: u32) -> VOut {
    let segment = vi / 2u;
    let endpoint = vi % 2u;
    let local_index = segment + endpoint;
    let base = u32(uniforms.data_offset);

    let v0 = data[base + segment];
    let v1 = data[base + segment + 1u];

    if (v0 != v0 || v1 != v1) {
        return VOut(vec4f(2.0, 2.0, 0.0, 1.0), uniforms.stroke_color);
    }

    let value = data[base + local_index];
    let x = sampleX(f32(local_index));
    let y = sampleY(value);
    return VOut(mapToClipSpace(x, y), uniforms.stroke_color);
}

@vertex
fn vs_fill(@builtin(vertex_index) vi: u32) -> VOut {
    let segment = vi / 6u;
    let corner = vi % 6u;

    let sample_offset = select(0u, 1u, corner == 1u || corner == 3u || corner == 4u);
    let is_zero = corner == 2u || corner == 4u || corner == 5u;

    let local_index = segment + sample_offset;
    let base = u32(uniforms.data_offset);

    let v0 = data[base + segment];
    let v1 = data[base + segment + 1u];

    if (v0 != v0 || v1 != v1) {
        return VOut(vec4f(2.0, 2.0, 0.0, 1.0), uniforms.fill_color);
    }

    let value = data[base + local_index];
    let x = sampleX(f32(local_index));
    let y = is_zero
        ? sampleY(uniforms.zero_level)
        : sampleY(value);

    return VOut(mapToClipSpace(x, y), uniforms.fill_color);
}

@fragment
fn fs_main(@location(0) @interpolate(flat) color: vec4f) -> @location(0) vec4f {
    return vec4f(color.rgb * color.a, color.a);
}
`;

    function isSupported() {
        return typeof navigator !== 'undefined' && !!navigator.gpu;
    }

    async function create(chartId) {
        if (!navigator.gpu) throw new Error('WebGPU not supported');

        const canvas = document.getElementById('gpu_' + chartId);
        if (!canvas) throw new Error('GPU canvas not found for chart ' + chartId);

        const adapter = await navigator.gpu.requestAdapter();
        if (!adapter) throw new Error('No WebGPU adapter');

        const device = await adapter.requestDevice();
        const context = canvas.getContext('webgpu');
        const format = navigator.gpu.getPreferredCanvasFormat();

        context.configure({
            device: device,
            format: format,
            alphaMode: 'premultiplied'
        });

        var pipelineDesc = {
            layout: 'auto',
            vertex: { module: null },
            fragment: {
                module: null,
                entryPoint: 'fs_main',
                targets: [{
                    format: format,
                    blend: {
                        color: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha' },
                        alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha' }
                    }
                }]
            }
        };

        var shaderModule = device.createShaderModule({ code: SHADER_CODE });

        var strokePipeline = device.createRenderPipeline(Object.assign({}, pipelineDesc, {
            vertex: { module: shaderModule, entryPoint: 'vs_stroke' },
            fragment: {
                module: shaderModule,
                entryPoint: 'fs_main',
                targets: [{
                    format: format,
                    blend: {
                        color: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha' },
                        alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha' }
                    }
                }]
            },
            primitive: { topology: 'line-list' }
        }));

        var fillPipeline = device.createRenderPipeline(Object.assign({}, pipelineDesc, {
            vertex: { module: shaderModule, entryPoint: 'vs_fill' },
            fragment: {
                module: shaderModule,
                entryPoint: 'fs_main',
                targets: [{
                    format: format,
                    blend: {
                        color: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha' },
                        alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha' }
                    }
                }]
            },
            primitive: { topology: 'triangle-list' }
        }));

        var s = {
            canvas: canvas,
            context: context,
            device: device,
            format: format,
            strokePipeline: strokePipeline,
            fillPipeline: fillPipeline,
            series: [],
            dataBuffer: null,
            scissor: { x: 0, y: 0, width: 0, height: 0 },
            ready: false,
            dirty: false,
            rafId: 0
        };

        state[chartId] = s;

        device.lost.then(function () {
            var st = state[chartId];
            if (st) {
                cancelAnimationFrame(st.rafId);
                delete state[chartId];
            }
        });

        s.rafId = requestAnimationFrame(function () { render(chartId); });
    }

    async function setData(chartId, streamRef, seriesCount, lengths) {
        var s = state[chartId];
        if (!s) return;

        if (s.series.length > 0) {
            for (var i = 0; i < s.series.length; i++) {
                s.series[i].uniformBuffer.destroy();
            }
            s.series = [];
        }

        if (s.dataBuffer) {
            s.dataBuffer.destroy();
            s.dataBuffer = null;
        }

        s.ready = false;

        var response = new Response(streamRef.stream);
        var buffer = await response.arrayBuffer();
        streamRef.dispose();

        s.dataBuffer = s.device.createBuffer({
            size: buffer.byteLength,
            usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST
        });
        s.device.queue.writeBuffer(s.dataBuffer, 0, buffer);

        var offsets = [0];
        for (var i = 0; i < lengths.length; i++) {
            offsets.push(offsets[i] + lengths[i]);
        }

        for (var i = 0; i < seriesCount; i++) {
            var uniformBuffer = s.device.createBuffer({
                size: 80,
                usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST
            });

            var uniform = new Float32Array(20);

            var bindGroupEntries = [
                { binding: 0, resource: { buffer: uniformBuffer } },
                { binding: 1, resource: { buffer: s.dataBuffer } }
            ];

            var strokeBindGroup = s.device.createBindGroup({
                layout: s.strokePipeline.getBindGroupLayout(0),
                entries: bindGroupEntries
            });

            var fillBindGroup = s.device.createBindGroup({
                layout: s.fillPipeline.getBindGroupLayout(0),
                entries: bindGroupEntries
            });

            s.series.push({
                uniformBuffer: uniformBuffer,
                uniform: uniform,
                strokeBindGroup: strokeBindGroup,
                fillBindGroup: fillBindGroup,
                dataOffset: offsets[i],
                strokeFirstVertex: 0,
                strokeVertexCount: 0,
                fillFirstVertex: 0,
                fillVertexCount: 0
            });
        }
    }

    function update(chartId, params) {
        var s = state[chartId];
        if (!s || s.series.length === 0) return;

        for (var i = 0; i < params.series.length && i < s.series.length; i++) {
            var p = params.series[i];
            var series = s.series[i];

            series.uniform.set(p.uniform);

            series.strokeFirstVertex = p.strokeFirstVertex;
            series.strokeVertexCount = p.strokeVertexCount;
            series.fillFirstVertex = p.fillFirstVertex;
            series.fillVertexCount = p.fillVertexCount;
        }

        s.scissor = params.scissor;
        s.ready = true;
        s.dirty = true;
    }

    function render(chartId) {
        var s = state[chartId];
        if (!s) return;

        try {
            var dpr = window.devicePixelRatio || 1;
            var displayWidth = Math.ceil(s.canvas.clientWidth * dpr);
            var displayHeight = Math.ceil(s.canvas.clientHeight * dpr);

            if (displayWidth === 0 || displayHeight === 0) {
                s.rafId = requestAnimationFrame(function () { render(chartId); });
                return;
            }

            if (s.canvas.width !== displayWidth || s.canvas.height !== displayHeight) {
                s.canvas.width = displayWidth;
                s.canvas.height = displayHeight;
            }

            if (!s.ready || s.series.length === 0) {
                s.rafId = requestAnimationFrame(function () { render(chartId); });
                return;
            }

            if (s.dirty) {
                for (var i = 0; i < s.series.length; i++) {
                    s.device.queue.writeBuffer(s.series[i].uniformBuffer, 0, s.series[i].uniform);
                }
                s.dirty = false;
            }

            var scissorX = Math.max(0, Math.floor(s.scissor.x));
            var scissorY = Math.max(0, Math.floor(s.scissor.y));
            var scissorW = Math.min(s.canvas.width - scissorX, Math.ceil(s.scissor.width));
            var scissorH = Math.min(s.canvas.height - scissorY, Math.ceil(s.scissor.height));

            if (scissorW <= 0 || scissorH <= 0) {
                s.rafId = requestAnimationFrame(function () { render(chartId); });
                return;
            }

            var encoder = s.device.createCommandEncoder();
            var pass = encoder.beginRenderPass({
                colorAttachments: [{
                    view: s.context.getCurrentTexture().createView(),
                    clearValue: { r: 0, g: 0, b: 0, a: 0 },
                    loadOp: 'clear',
                    storeOp: 'store'
                }]
            });

            pass.setScissorRect(scissorX, scissorY, scissorW, scissorH);

            for (var i = 0; i < s.series.length; i++) {
                var series = s.series[i];

                if (series.strokeVertexCount > 0) {
                    pass.setPipeline(s.strokePipeline);
                    pass.setBindGroup(0, series.strokeBindGroup);
                    pass.draw(series.strokeVertexCount, 1, series.strokeFirstVertex, 0);
                }

                if (series.fillVertexCount > 0) {
                    pass.setPipeline(s.fillPipeline);
                    pass.setBindGroup(0, series.fillBindGroup);
                    pass.draw(series.fillVertexCount, 1, series.fillFirstVertex, 0);
                }
            }

            pass.end();
            s.device.queue.submit([encoder.finish()]);
        } catch (e) {
        }

        s.rafId = requestAnimationFrame(function () { render(chartId); });
    }

    function dispose(chartId) {
        var s = state[chartId];
        if (!s) return;

        cancelAnimationFrame(s.rafId);

        for (var i = 0; i < s.series.length; i++) {
            s.series[i].uniformBuffer.destroy();
        }

        if (s.dataBuffer) s.dataBuffer.destroy();

        delete state[chartId];
    }

    nexus.chart = nexus.chart || {};
    nexus.chart.gpu = {
        isSupported: isSupported,
        create: create,
        setData: setData,
        update: update,
        dispose: dispose
    };
})();
