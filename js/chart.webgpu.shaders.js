(function () {
    const ns = window.__nexusChartWebGpu = {};
    const uniformBufferSize = 96;
    const fillVerticesPerSegment = 6;
    const lineVerticesPerSegment = 18;
    const decimationWorkgroupSize = 64;
    const decimationFactor = 4;
    const decimationBucketsPerPixel = 2;
    const maxDecimationBuckets = 8192;
    const rangeWorkgroupSize = 256;
    const maxRangeWorkgroups = 1024;
    const defaultCacheBudget = 512 * 1024 * 1024;
    const overviewBucketSize = 256;
    const reducedPointsPerBucket = 3;
    const syntheticStreamChunkLength = 4 * 1024 * 1024;
    const rawChunkLength = 1024 * 1024;

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
    xOrigin: f32,
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
        x = points[index].x - uniforms.xOrigin;
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

fn isNonFinite(x: f32) -> bool {
    // Treat both infinities and NaNs as gaps. The exponent bit test is reliable across drivers.
    let bits = bitcast<u32>(x);
    return (bits & 0x7f800000u) == 0x7f800000u;
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

    if (isNonFinite(valueA) || isNonFinite(valueB) || uniforms.axis.y == 0.0) {
        return emptyVertex();
    }

    let a = dataPoint(index);
    let b = dataPoint(index + 1u);

    if (uniforms.mode == 0u) {
        var out: VertexOut;
        out.position = toNdc(fillVertex(a, b, local));
        out.color = vec4f(uniforms.color.rgb, uniforms.color.a * uniforms.fillOpacity);
        return out;
    }

    return lineVertex(a, b, local);
}

@fragment
fn fragmentMain(in: VertexOut) -> @location(0) vec4f {
    return in.color;
}
`;

    const overviewShader = `
struct Params {
    globalOffset: u32,
    sourceLength: u32,
    outputBucket: u32,
    _pad: u32,
};

@group(0) @binding(0) var<storage, read> source: array<f32>;
@group(0) @binding(1) var<storage, read_write> output: array<vec2f>;
@group(0) @binding(2) var<uniform> params: Params;

var<workgroup> minimums: array<f32, ${overviewBucketSize}>;
var<workgroup> maximums: array<f32, ${overviewBucketSize}>;
var<workgroup> minimumIndices: array<u32, ${overviewBucketSize}>;
var<workgroup> maximumIndices: array<u32, ${overviewBucketSize}>;
var<workgroup> valid: array<u32, ${overviewBucketSize}>;
var<workgroup> nanSeen: array<u32, ${overviewBucketSize}>;
var<workgroup> nanIndices: array<u32, ${overviewBucketSize}>;
var<workgroup> nanRunCounts: array<u32, ${overviewBucketSize}>;

fn isNonFinite(x: f32) -> bool {
    let bits = bitcast<u32>(x);
    return (bits & 0x7f800000u) == 0x7f800000u;
}

@compute @workgroup_size(${overviewBucketSize})
fn reduceOverview(@builtin(workgroup_id) groupId: vec3u, @builtin(local_invocation_id) localId: vec3u) {
    let lane = localId.x;
    let localIndex = groupId.x * ${overviewBucketSize}u + lane;
    var value = 0.0;
    var hasValue = 0u;
    var hasNan = 0u;
    var nanRunCount = 0u;

    if (localIndex < params.sourceLength) {
        value = source[localIndex];
        hasNan = select(0u, 1u, isNonFinite(value));
        hasValue = select(1u, 0u, isNonFinite(value));
        if (hasNan != 0u && (lane == 0u || !isNonFinite(source[localIndex - 1u]))) {
            nanRunCount = 1u;
        }
    }

    minimums[lane] = value;
    maximums[lane] = value;
    minimumIndices[lane] = localIndex;
    maximumIndices[lane] = localIndex;
    valid[lane] = hasValue;
    nanSeen[lane] = hasNan;
    nanIndices[lane] = localIndex;
    nanRunCounts[lane] = nanRunCount;
    workgroupBarrier();

    var stride = ${overviewBucketSize / 2}u;
    while (stride > 0u) {
        if (lane < stride) {
            let other = lane + stride;
            if (valid[other] != 0u) {
                if (valid[lane] == 0u || minimums[other] < minimums[lane]) {
                    minimums[lane] = minimums[other];
                    minimumIndices[lane] = minimumIndices[other];
                }
                if (valid[lane] == 0u || maximums[other] > maximums[lane]) {
                    maximums[lane] = maximums[other];
                    maximumIndices[lane] = maximumIndices[other];
                }
                valid[lane] = 1u;
            }
            if (nanSeen[other] != 0u && (nanSeen[lane] == 0u || nanIndices[other] < nanIndices[lane])) {
                nanIndices[lane] = nanIndices[other];
            }
            nanSeen[lane] |= nanSeen[other];
            nanRunCounts[lane] = min(2u, nanRunCounts[lane] + nanRunCounts[other]);
        }
        workgroupBarrier();
        stride /= 2u;
    }

    if (lane == 0u) {
        let outputIndex = (params.outputBucket + groupId.x) * ${reducedPointsPerBucket}u;
        if (valid[0] == 0u || nanRunCounts[0] >= 2u) {
            let nan = source[nanIndices[0]];
            let point = vec2f(f32(params.globalOffset + nanIndices[0]) / ${overviewBucketSize}.0, nan);
            output[outputIndex] = point;
            output[outputIndex + 1u] = point;
            output[outputIndex + 2u] = point;
        } else {
            var firstIndex = minimumIndices[0];
            var secondIndex = maximumIndices[0];
            var firstPoint = vec2f(f32(params.globalOffset + firstIndex) / ${overviewBucketSize}.0, minimums[0]);
            var secondPoint = vec2f(f32(params.globalOffset + secondIndex) / ${overviewBucketSize}.0, maximums[0]);
            if (secondIndex < firstIndex) {
                let swapIndex = firstIndex; firstIndex = secondIndex; secondIndex = swapIndex;
                let swapPoint = firstPoint; firstPoint = secondPoint; secondPoint = swapPoint;
            }
            var thirdIndex = secondIndex;
            var thirdPoint = secondPoint;
            if (nanSeen[0] != 0u) {
                thirdIndex = nanIndices[0];
                thirdPoint = vec2f(f32(params.globalOffset + thirdIndex) / ${overviewBucketSize}.0, source[thirdIndex]);
                if (thirdIndex < secondIndex) {
                    let swapIndex = secondIndex; secondIndex = thirdIndex; thirdIndex = swapIndex;
                    let swapPoint = secondPoint; secondPoint = thirdPoint; thirdPoint = swapPoint;
                }
                if (secondIndex < firstIndex) {
                    let swapIndex = firstIndex; firstIndex = secondIndex; secondIndex = swapIndex;
                    let swapPoint = firstPoint; firstPoint = secondPoint; secondPoint = swapPoint;
                }
            }
            output[outputIndex] = firstPoint;
            output[outputIndex + 1u] = secondPoint;
            output[outputIndex + 2u] = thirdPoint;
        }
    }
}
`;

    const pointDecimationShader = `
struct Params {
    first: u32,
    visibleLength: u32,
    bucketCount: u32,
    sourceLength: u32,
};
@group(0) @binding(0) var<storage, read> source: array<vec2f>;
@group(0) @binding(1) var<storage, read_write> output: array<vec2f>;
@group(0) @binding(2) var<uniform> params: Params;
var<workgroup> minimums: array<vec2f, ${decimationWorkgroupSize}>;
var<workgroup> maximums: array<vec2f, ${decimationWorkgroupSize}>;
var<workgroup> valid: array<u32, ${decimationWorkgroupSize}>;
var<workgroup> nanSeen: array<u32, ${decimationWorkgroupSize}>;
var<workgroup> minimumIndices: array<u32, ${decimationWorkgroupSize}>;
var<workgroup> maximumIndices: array<u32, ${decimationWorkgroupSize}>;
var<workgroup> nanIndices: array<u32, ${decimationWorkgroupSize}>;
var<workgroup> nanRunCounts: array<u32, ${decimationWorkgroupSize}>;
fn isNonFinite(x: f32) -> bool { let b = bitcast<u32>(x); return (b & 0x7f800000u) == 0x7f800000u; }
@compute @workgroup_size(${decimationWorkgroupSize})
fn decimatePoints(@builtin(workgroup_id) groupId: vec3u, @builtin(local_invocation_id) localId: vec3u) {
    let bucket = groupId.x;
    let lane = localId.x;
    let q = params.visibleLength / params.bucketCount;
    let r = params.visibleLength % params.bucketCount;
    let start = params.first + bucket * q + min(bucket, r);
    let next = bucket + 1u;
    let end = min(params.first + next * q + min(next, r), params.sourceLength);
    var minimum = vec2f(0.0);
    var maximum = vec2f(0.0);
    var hasValue = 0u;
    var hasNan = 0u;
    var minimumIndex = 0u;
    var maximumIndex = 0u;
    var nanIndex = 0u;
    var nanRunCount = 0u;
    var index = start + lane;
    while (index < end) {
        let point = source[index];
        if (isNonFinite(point.y)) {
            if (hasNan == 0u || index < nanIndex) { nanIndex = index; }
            hasNan = 1u;
            if (index == start || !isNonFinite(source[index - 1u].y)) { nanRunCount = min(2u, nanRunCount + 1u); }
        }
        else {
            if (hasValue == 0u || point.y < minimum.y) { minimum = point; minimumIndex = index; }
            if (hasValue == 0u || point.y > maximum.y) { maximum = point; maximumIndex = index; }
            hasValue = 1u;
        }
        index += ${decimationWorkgroupSize}u;
    }
    minimums[lane] = minimum; maximums[lane] = maximum; valid[lane] = hasValue; nanSeen[lane] = hasNan;
    minimumIndices[lane] = minimumIndex; maximumIndices[lane] = maximumIndex; nanIndices[lane] = nanIndex;
    nanRunCounts[lane] = nanRunCount;
    workgroupBarrier();
    var stride = ${decimationWorkgroupSize / 2}u;
    while (stride > 0u) {
        if (lane < stride) {
            let other = lane + stride;
            if (valid[other] != 0u) {
                if (valid[lane] == 0u || minimums[other].y < minimums[lane].y) { minimums[lane] = minimums[other]; minimumIndices[lane] = minimumIndices[other]; }
                if (valid[lane] == 0u || maximums[other].y > maximums[lane].y) { maximums[lane] = maximums[other]; maximumIndices[lane] = maximumIndices[other]; }
                valid[lane] = 1u;
            }
            if (nanSeen[other] != 0u && (nanSeen[lane] == 0u || nanIndices[other] < nanIndices[lane])) { nanIndices[lane] = nanIndices[other]; }
            nanSeen[lane] |= nanSeen[other];
            nanRunCounts[lane] = min(2u, nanRunCounts[lane] + nanRunCounts[other]);
        }
        workgroupBarrier(); stride /= 2u;
    }
    if (lane == 0u) {
        let outIndex = bucket * ${reducedPointsPerBucket}u + 1u;
        if (bucket == 0u) { output[0] = source[params.first]; }
        if (valid[0] == 0u || nanRunCounts[0] >= 2u) {
            let point = source[nanIndices[0]];
            output[outIndex] = point; output[outIndex + 1u] = point; output[outIndex + 2u] = point;
        } else {
            var firstIndex = minimumIndices[0]; var secondIndex = maximumIndices[0];
            var firstPoint = minimums[0]; var secondPoint = maximums[0];
            if (secondIndex < firstIndex) {
                let swapIndex = firstIndex; firstIndex = secondIndex; secondIndex = swapIndex;
                let swapPoint = firstPoint; firstPoint = secondPoint; secondPoint = swapPoint;
            }
            var thirdIndex = secondIndex; var thirdPoint = secondPoint;
            if (nanSeen[0] != 0u) {
                thirdIndex = nanIndices[0]; thirdPoint = source[thirdIndex];
                if (thirdIndex < secondIndex) {
                    let swapIndex = secondIndex; secondIndex = thirdIndex; thirdIndex = swapIndex;
                    let swapPoint = secondPoint; secondPoint = thirdPoint; thirdPoint = swapPoint;
                }
                if (secondIndex < firstIndex) {
                    let swapIndex = firstIndex; firstIndex = secondIndex; secondIndex = swapIndex;
                    let swapPoint = firstPoint; firstPoint = secondPoint; secondPoint = swapPoint;
                }
            }
            output[outIndex] = firstPoint; output[outIndex + 1u] = secondPoint; output[outIndex + 2u] = thirdPoint;
        }
        if (bucket + 1u == params.bucketCount) { output[outIndex + 3u] = source[params.first + params.visibleLength - 1u]; }
    }
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
var<workgroup> nanRunCounts: array<u32, ${decimationWorkgroupSize}>;

fn isNonFinite(x: f32) -> bool {
    let bits = bitcast<u32>(x);
    return (bits & 0x7f800000u) == 0x7f800000u;
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
    var nanRunCount = 0u;
    var index = start + lane;

    while (index < end) {
        let value = source[index];

        if (isNonFinite(value)) {
            if (hasNan == 0u || index < nanIndex) {
                nanIndex = index;
            }

            hasNan = 1u;
            if (index == start || !isNonFinite(source[index - 1u])) {
                nanRunCount = min(2u, nanRunCount + 1u);
            }
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
    nanRunCounts[lane] = nanRunCount;
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
            nanRunCounts[lane] = min(2u, nanRunCounts[lane] + nanRunCounts[lane + stride]);
        }

        workgroupBarrier();
        stride /= 2u;
    }

    if (lane == 0u) {
        let outputIndex = bucket * ${reducedPointsPerBucket}u + 1u;

        if (bucket == 0u) {
            output[0] = vec2f(0.0, source[params.first]);
        }

        if (valid[0] == 0u || nanRunCounts[0] >= 2u) {
            let nan = source[nanIndices[0]];
            let point = vec2f(f32(nanIndices[0] - params.first), nan);
            output[outputIndex] = point;
            output[outputIndex + 1u] = point;
            output[outputIndex + 2u] = point;
        } else {
            var firstIndex = minimumIndices[0]; var secondIndex = maximumIndices[0];
            var firstPoint = vec2f(f32(firstIndex - params.first), minimums[0]);
            var secondPoint = vec2f(f32(secondIndex - params.first), maximums[0]);
            if (secondIndex < firstIndex) {
                let swapIndex = firstIndex; firstIndex = secondIndex; secondIndex = swapIndex;
                let swapPoint = firstPoint; firstPoint = secondPoint; secondPoint = swapPoint;
            }
            var thirdIndex = secondIndex; var thirdPoint = secondPoint;
            if (nanSeen[0] != 0u) {
                thirdIndex = nanIndices[0]; thirdPoint = vec2f(f32(thirdIndex - params.first), source[thirdIndex]);
                if (thirdIndex < secondIndex) {
                    let swapIndex = secondIndex; secondIndex = thirdIndex; thirdIndex = swapIndex;
                    let swapPoint = secondPoint; secondPoint = thirdPoint; thirdPoint = swapPoint;
                }
                if (secondIndex < firstIndex) {
                    let swapIndex = firstIndex; firstIndex = secondIndex; secondIndex = swapIndex;
                    let swapPoint = firstPoint; firstPoint = secondPoint; secondPoint = swapPoint;
                }
            }
            output[outputIndex] = firstPoint;
            output[outputIndex + 1u] = secondPoint;
            output[outputIndex + 2u] = thirdPoint;
        }

        if (bucket + 1u == params.bucketCount) {
            let last = params.first + params.visibleLength - 1u;
            output[outputIndex + 3u] = vec2f(f32(params.visibleLength - 1u), source[last]);
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

    Object.assign(ns, {
        uniformBufferSize, fillVerticesPerSegment, lineVerticesPerSegment,
        decimationWorkgroupSize, decimationFactor, decimationBucketsPerPixel,
        maxDecimationBuckets, rangeWorkgroupSize, maxRangeWorkgroups,
        defaultCacheBudget, overviewBucketSize, reducedPointsPerBucket,
        syntheticStreamChunkLength, rawChunkLength, shader, overviewShader,
        pointDecimationShader, decimationShader, rangeShader,
    });
})();
