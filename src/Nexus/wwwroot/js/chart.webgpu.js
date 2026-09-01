(function () {
    const instances = new Map();
    const pendingInstances = new Map();
    const lifecycleEpochs = new Map();
    const failureStates = new Map();
    const dotNetHelpers = new Map();
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
    const configuredCacheBudgets = new Map();
    let sharedGpu = null;
    let pendingSharedGpu = null;
    let sharedGpuGeneration = 0;

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

fn isNan(x: f32) -> bool {
    let bits = bitcast<u32>(x);
    return (bits & 0x7f800000u) == 0x7f800000u && (bits & 0x007fffffu) != 0u;
}

@compute @workgroup_size(${overviewBucketSize})
fn reduceOverview(@builtin(workgroup_id) groupId: vec3u, @builtin(local_invocation_id) localId: vec3u) {
    let lane = localId.x;
    let localIndex = groupId.x * ${overviewBucketSize}u + lane;
    var value = 0.0;
    var hasValue = 0u;
    var hasNan = 0u;

    if (localIndex < params.sourceLength) {
        value = source[localIndex];
        hasNan = select(0u, 1u, isNan(value));
        hasValue = select(1u, 0u, isNan(value));
    }

    minimums[lane] = value;
    maximums[lane] = value;
    minimumIndices[lane] = localIndex;
    maximumIndices[lane] = localIndex;
    valid[lane] = hasValue;
    nanSeen[lane] = hasNan;
    nanIndices[lane] = localIndex;
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
        }
        workgroupBarrier();
        stride /= 2u;
    }

    if (lane == 0u) {
        let outputIndex = (params.outputBucket + groupId.x) * ${reducedPointsPerBucket}u;
        if (valid[0] == 0u) {
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
fn isNan(x: f32) -> bool { let b = bitcast<u32>(x); return (b & 0x7f800000u) == 0x7f800000u && (b & 0x007fffffu) != 0u; }
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
    var index = start + lane;
    while (index < end) {
        let point = source[index];
        if (isNan(point.y)) {
            if (hasNan == 0u || index < nanIndex) { nanIndex = index; }
            hasNan = 1u;
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
        }
        workgroupBarrier(); stride /= 2u;
    }
    if (lane == 0u) {
        let outIndex = bucket * ${reducedPointsPerBucket}u + 1u;
        if (bucket == 0u) { output[0] = source[params.first]; }
        if (valid[0] == 0u) {
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
        let outputIndex = bucket * ${reducedPointsPerBucket}u + 1u;

        if (bucket == 0u) {
            output[0] = vec2f(0.0, source[params.first]);
        }

        if (valid[0] == 0u) {
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
            (valueOf(color, 'Alpha') ?? 255) / 255,
        ];
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

    function getCanvasContext(instance, target, canvas) {
        const configured = instance.canvasContexts.get(target);

        if (configured?.canvas === canvas)
            return configured.context;

        configured?.context.unconfigure?.();
        instance.previewRenderKeys.delete(target);
        const context = canvas.getContext('webgpu');
        context.configure({
            device: instance.device,
            format: instance.format,
            alphaMode: 'premultiplied',
        });
        instance.canvasContexts.set(target, { canvas, context });
        return context;
    }

    function releaseCanvasContext(instance, target) {
        const configured = instance.canvasContexts.get(target);
        configured?.context.unconfigure?.();
        instance.canvasContexts.delete(target);
        instance.previewRenderKeys.delete(target);
    }

    function getReducedOutputLength(bucketCount) {
        return bucketCount * reducedPointsPerBucket + 2;
    }

    function getLifecycleEpoch(chartId) {
        return lifecycleEpochs.get(chartId) ?? 0;
    }

    function advanceLifecycleEpoch(chartId) {
        const epoch = getLifecycleEpoch(chartId) + 1;
        lifecycleEpochs.set(chartId, epoch);
        return epoch;
    }

    function reportFailure(chartId, title, message) {
        const current = failureStates.get(chartId);
        if (current?.title === title && current?.message === message)
            return;

        failureStates.set(chartId, { title, message });
        const helper = dotNetHelpers.get(chartId);
        helper?.invokeMethodAsync('WebGpuFailed', title, message)
            .catch(error => console.error('[chart-webgpu] failure callback failed', error));
    }

    function destroyInstance(instance, reason) {
        if (!instance || instance.disposed)
            return;

        instance.disposed = true;

        for (const id of [...instance.generationJobs.keys()])
            cancelGeneration(instance, id, reason);

        for (const request of instance.rawRequests.values()) {
            request.worker.postMessage({ type: 'cancel', requestId: request.requestId });
            request.worker.terminate();
            instance.rawReservedBytes -= request.byteLength;
            request.reject(new Error(reason));
        }
        instance.rawRequests.clear();

        for (const [key, chunk] of instance.rawChunks)
            destroyRawChunk(instance, key, chunk);

        for (const cached of instance.seriesBuffers.values())
            destroySeriesBuffer(instance, cached);
        instance.seriesBuffers.clear();

        for (const upload of instance.uploadSessions.values())
            upload.buffer?.destroy();
        instance.uploadSessions.clear();

        for (const targetResources of instance.targetResources.values()) {
            for (const resources of targetResources)
                resources.uniformBuffer.destroy();
        }
        instance.targetResources.clear();

        for (const { context } of instance.canvasContexts.values())
            context.unconfigure?.();
        instance.canvasContexts.clear();

    }

    async function getSharedGpu() {
        if (sharedGpu)
            return sharedGpu;

        if (pendingSharedGpu)
            return pendingSharedGpu;

        const generation = ++sharedGpuGeneration;
        pendingSharedGpu = Promise.resolve().then(async () => {
            if (!navigator.gpu)
                throw new Error('WebGPU is not available. Use a current WebGPU-capable browser and ensure hardware acceleration is enabled.');

            const adapter = await navigator.gpu.requestAdapter();
            if (!adapter)
                throw new Error('No compatible GPU adapter was found. Ensure hardware acceleration is enabled, then retry.');

            const device = await adapter.requestDevice({
                requiredLimits: {
                    maxBufferSize: adapter.limits.maxBufferSize,
                    maxStorageBufferBindingSize: adapter.limits.maxStorageBufferBindingSize,
                },
            });
            let bundle;
            try {
                if (generation !== sharedGpuGeneration)
                    throw new Error('WebGPU initialization was superseded.');

                const format = navigator.gpu.getPreferredCanvasFormat();
                const module = device.createShaderModule({ code: shader });
                const decimationModule = device.createShaderModule({ code: decimationShader });
                const rangeModule = device.createShaderModule({ code: rangeShader });
                const overviewModule = device.createShaderModule({ code: overviewShader });
                const pointDecimationModule = device.createShaderModule({ code: pointDecimationShader });
                bundle = {
                    generation,
                    device,
                    format,
                    pipeline: device.createRenderPipeline({
                        layout: 'auto',
                        vertex: { module, entryPoint: 'vertexMain' },
                        fragment: {
                            module,
                            entryPoint: 'fragmentMain',
                            targets: [{
                                format,
                                blend: {
                                    color: { srcFactor: 'src-alpha', dstFactor: 'one-minus-src-alpha', operation: 'add' },
                                    alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha', operation: 'add' },
                                },
                            }],
                        },
                        primitive: { topology: 'triangle-list' },
                    }),
                    decimationPipeline: device.createComputePipeline({
                        layout: 'auto', compute: { module: decimationModule, entryPoint: 'decimate' },
                    }),
                    rangePipeline: device.createComputePipeline({
                        layout: 'auto', compute: { module: rangeModule, entryPoint: 'reduceRange' },
                    }),
                    overviewPipeline: device.createComputePipeline({
                        layout: 'auto', compute: { module: overviewModule, entryPoint: 'reduceOverview' },
                    }),
                    pointDecimationPipeline: device.createComputePipeline({
                        layout: 'auto', compute: { module: pointDecimationModule, entryPoint: 'decimatePoints' },
                    }),
                };
            } catch (error) {
                device.destroy();
                throw error;
            }

            device.lost.then(info => {
                if (sharedGpu !== bundle)
                    return;

                sharedGpu = null;
                sharedGpuGeneration++;
                const detail = info.message ? ` ${info.message}` : '';
                for (const [chartId, instance] of [...instances]) {
                    if (instance.gpuGeneration !== bundle.generation)
                        continue;

                    advanceLifecycleEpoch(chartId);
                    instances.delete(chartId);
                    destroyInstance(instance, `WebGPU device lost for chart ${chartId}`);
                    reportFailure(chartId, 'GPU connection lost', `The browser lost access to the GPU.${detail} Retry the chart to recreate its GPU resources.`);
                }
            }).catch(error => console.error('[chart-webgpu] device loss handler failed', error));

            sharedGpu = bundle;
            return bundle;
        }).finally(() => {
            pendingSharedGpu = null;
        });

        return pendingSharedGpu;
    }

    async function getInstance(chartId) {
        let instance = instances.get(chartId);

        if (instance)
            return instance;

        const failure = failureStates.get(chartId);
        if (failure)
            throw new Error(failure.message);

        let pending = pendingInstances.get(chartId);

        if (pending)
            return pending.promise;

        const epoch = getLifecycleEpoch(chartId);
        const entry = { epoch, promise: null };
        entry.promise = Promise.resolve().then(async () => {
            try {
                if (getLifecycleEpoch(chartId) !== epoch)
                    return null;

                const canvas = document.getElementById(`series_${chartId}`);

                if (!canvas)
                    throw new Error('The chart canvas is unavailable.');

                const gpu = await getSharedGpu();
                if (getLifecycleEpoch(chartId) !== epoch)
                    return null;

                instance = {
                    chartId,
                    ...gpu,
                    gpuGeneration: gpu.generation,
                    seriesBuffers: new Map(),
                    uploadSessions: new Map(),
                    uploadToken: 0,
                    targetResources: new Map(),
                    canvasContexts: new Map(),
                    previewRenderKeys: new Map(),
                    uploadGenerations: new Map(),
                    cacheBudget: configuredCacheBudgets.get(chartId) ?? defaultCacheBudget,
                    persistentBytes: 0,
                    rawCacheBytes: 0,
                    rawReservedBytes: 0,
                    generationReservedBytes: 0,
                    auxiliaryBytes: 0,
                    rawChunks: new Map(),
                    rawRequests: new Map(),
                    generationJobs: new Map(),
                    workerRequestId: 0,
                    lastPayloads: new Map(),
                    disposed: false,
                };

                if (getLifecycleEpoch(chartId) !== epoch) {
                    destroyInstance(instance, `Chart ${chartId} initialization was superseded`);
                    return null;
                }

                instances.set(chartId, instance);
                return instance;
            } catch (error) {
                if (!failureStates.has(chartId) && getLifecycleEpoch(chartId) === epoch) {
                    const unavailable = !navigator.gpu || error?.message?.startsWith('No compatible GPU adapter');
                    reportFailure(
                        chartId,
                        unavailable ? 'WebGPU unavailable' : 'WebGPU initialization failed',
                        unavailable ? error.message : `The chart could not initialize WebGPU: ${error?.message ?? error}. Check browser hardware acceleration, then retry.`);
                }

                throw error;
            } finally {
                if (pendingInstances.get(chartId) === entry)
                    pendingInstances.delete(chartId);

                if (!dotNetHelpers.has(chartId) && !instances.has(chartId))
                    lifecycleEpochs.delete(chartId);
            }
        });

        pendingInstances.set(chartId, entry);
        return entry.promise;
    }

    function getSeriesKey(id, version, length) {
        return `${id}:${version}:${length}`;
    }

    function destroySeriesBuffer(instance, cached) {
        cached.buffer.destroy();

        if (cached.pointBuffer !== cached.buffer)
            cached.pointBuffer.destroy();

        for (const decimation of cached.decimations?.values() ?? []) {
            decimation.outputBuffer.destroy();
            decimation.paramsBuffer.destroy();
            instance.auxiliaryBytes -= decimation.byteLength;
        }
    }

    function destroyRawChunk(instance, key, chunk) {
        destroySeriesBuffer(instance, chunk);
        instance.rawChunks.delete(key);
        instance.rawCacheBytes -= chunk.byteLength;
    }

    function evictRawChunks(instance, requiredBytes, protectedKeys = new Set()) {
        const candidates = [...instance.rawChunks.entries()]
            .filter(([key]) => !protectedKeys.has(key))
            .sort((a, b) => a[1].lastUsed - b[1].lastUsed);

        while (instance.rawCacheBytes + instance.rawReservedBytes + requiredBytes > instance.cacheBudget && candidates.length) {
            const [key, chunk] = candidates.shift();
            destroyRawChunk(instance, key, chunk);
        }

        if (requiredBytes > 0 && instance.rawCacheBytes + instance.rawReservedBytes + requiredBytes > instance.cacheBudget)
            throw new Error(`Chart raw-detail cache budget (${instance.cacheBudget} bytes) cannot fit a ${requiredBytes}-byte chunk`);
    }

    function removeRawSeries(instance, id) {
        for (const [key, request] of instance.rawRequests) {
            if (request.id === id) {
                request.worker.postMessage({ type: 'cancel', requestId: request.requestId });
                request.worker.terminate();
                instance.rawReservedBytes -= request.byteLength;
                request.reject(new Error(`Raw chunk request superseded for series ${id}`));
                instance.rawRequests.delete(key);
            }
        }

        for (const [key, chunk] of instance.rawChunks) {
            if (chunk.id === id)
                destroyRawChunk(instance, key, chunk);
        }
    }

    function cancelGeneration(instance, id, reason) {
        const job = instance.generationJobs.get(id);
        if (!job)
            return;

        job.worker.postMessage({ type: 'cancel', requestId: job.requestId });
        job.worker.terminate();
        job.reject(new Error(reason));
    }

    function synchronizeSeries(instance, activeIds) {
        const active = new Set(activeIds);

        for (const [key, cached] of instance.seriesBuffers) {
            if (active.has(cached.id))
                continue;

            instance.persistentBytes -= cached.byteLength ?? 0;
            destroySeriesBuffer(instance, cached);
            instance.seriesBuffers.delete(key);
            removeRawSeries(instance, cached.id);
            cancelGeneration(instance, cached.id, `Series ${cached.id} was removed`);
            instance.uploadGenerations.delete(cached.id);
        }

        for (const id of instance.generationJobs.keys()) {
            if (!active.has(id))
                cancelGeneration(instance, id, `Series ${id} was removed`);
        }

        for (const [token, upload] of instance.uploadSessions) {
            if (active.has(upload.id))
                continue;

            upload.buffer?.destroy();
            instance.uploadSessions.delete(token);
        }
    }

    function createSeriesUpload(instance, id, version, length) {
        if (!Number.isSafeInteger(length) || length < 0)
            throw new Error(`Series length must be a non-negative safe integer (received ${length})`);

        const byteLength = length * Float32Array.BYTES_PER_ELEMENT;
        if (!Number.isSafeInteger(byteLength))
            throw new Error(`Series byte length is not a safe integer (${byteLength})`);
        const deviceLimit = Math.min(instance.device.limits.maxBufferSize, instance.device.limits.maxStorageBufferBindingSize);
        if (byteLength > deviceLimit)
            throw new Error(`Series requires ${byteLength} bytes, exceeding the GPU storage buffer limit of ${deviceLimit} bytes`);

        for (const [token, upload] of instance.uploadSessions) {
            if (upload.id !== id)
                continue;

            upload.buffer?.destroy();
            instance.uploadSessions.delete(token);
        }

        const generation = (instance.uploadGenerations.get(id) ?? 0) + 1;
        instance.uploadGenerations.set(id, generation);
        removeRawSeries(instance, id);
        const token = ++instance.uploadToken;
        const buffer = length >= 2
            ? instance.device.createBuffer({
                size: byteLength,
                usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
            })
            : null;
        instance.uploadSessions.set(token, { id, version, length, byteLength, buffer, generation, writtenBytes: 0 });
        return token;
    }

    async function appendSeriesUploadAsync(chartId, token, byteOffset, streamReference) {
        const instance = await getInstance(chartId);
        const upload = instance?.uploadSessions.get(token);
        if (!upload)
            throw new Error(`Series upload ${token} is no longer active`);

        const bytes = new Uint8Array(await streamReference.arrayBuffer());
        if (instances.get(chartId) !== instance || instance.uploadSessions.get(token) !== upload)
            throw new Error(`Series upload ${token} was superseded`);
        if (!Number.isSafeInteger(byteOffset) || byteOffset !== upload.writtenBytes)
            throw new Error(`Series upload ${token} expected byte offset ${upload.writtenBytes}, received ${byteOffset}`);
        if (bytes.byteLength % Float32Array.BYTES_PER_ELEMENT !== 0 || byteOffset + bytes.byteLength > upload.byteLength)
            throw new Error(`Series upload ${token} contains invalid or excess data`);

        if (bytes.byteLength > 0 && upload.buffer)
            instance.device.queue.writeBuffer(upload.buffer, byteOffset, bytes);
        upload.writtenBytes += bytes.byteLength;
    }

    async function completeSeriesUploadAsync(chartId, token) {
        const instance = await getInstance(chartId);
        const upload = instance?.uploadSessions.get(token);
        if (!upload)
            throw new Error(`Series upload ${token} is no longer active`);
        if (upload.writtenBytes !== upload.byteLength)
            throw new Error(`Series upload ${token} is incomplete (${upload.writtenBytes} of ${upload.byteLength} bytes)`);

        const range = upload.buffer
            ? await calculateSeriesRangeAsync(instance, upload.buffer, upload.length)
            : { hasValue: false, minimum: 0, maximum: 0 };
        if (instances.get(chartId) !== instance || instance.uploadSessions.get(token) !== upload)
            throw new Error(`Series upload ${token} was superseded`);

        for (const [existingKey, existing] of instance.seriesBuffers) {
            if (existing.id === upload.id) {
                instance.persistentBytes -= existing.byteLength ?? 0;
                destroySeriesBuffer(instance, existing);
                instance.seriesBuffers.delete(existingKey);
            }
        }

        if (upload.buffer) {
            const key = getSeriesKey(upload.id, upload.version, upload.length);
            const cachedSeries = {
                id: upload.id,
                buffer: upload.buffer,
                pointBuffer: upload.buffer,
                length: upload.length,
                byteLength: upload.byteLength,
                dataMode: 0,
                decimations: new Map(),
            };
            instance.seriesBuffers.set(key, cachedSeries);
            instance.persistentBytes += upload.byteLength;
        }

        instance.uploadSessions.delete(token);
        return range;
    }

    function abortSeriesUpload(chartId, token) {
        const instance = instances.get(chartId);
        const upload = instance?.uploadSessions.get(token);
        if (!upload)
            return;

        upload.buffer?.destroy();
        instance.uploadSessions.delete(token);
    }

    async function generateSyntheticSeriesAsync(chartId, id, version, length, kind) {
        const instance = await getInstance(chartId);

        if (!instance)
            throw new Error(`WebGPU instance unavailable for chart ${chartId}`);

        if (!Number.isSafeInteger(length) || length < 2 || length > 1000000000)
            throw new Error(`Synthetic series length must be an integer between 2 and 1,000,000,000 (received ${length})`);

        const generation = (instance.uploadGenerations.get(id) ?? 0) + 1;
        instance.uploadGenerations.set(id, generation);
        for (const [token, upload] of instance.uploadSessions) {
            if (upload.id !== id)
                continue;

            upload.buffer?.destroy();
            instance.uploadSessions.delete(token);
        }
        cancelGeneration(instance, id, `Synthetic generation superseded for series ${id}`);
        removeRawSeries(instance, id);
        const overviewBucketCount = Math.ceil(length / overviewBucketSize);
        const overviewLength = overviewBucketCount * reducedPointsPerBucket;
        const overviewBytes = overviewLength * 2 * Float32Array.BYTES_PER_ELEMENT;
        const deviceLimit = Math.min(instance.device.limits.maxBufferSize, instance.device.limits.maxStorageBufferBindingSize);

        if (overviewBytes > deviceLimit)
            throw new Error(`Persistent overview requires ${overviewBytes} bytes, exceeding the GPU storage buffer limit of ${deviceLimit} bytes`);

        const transientBytes = Math.min(syntheticStreamChunkLength, length) * Float32Array.BYTES_PER_ELEMENT;
        if (transientBytes > deviceLimit)
            throw new Error(`Synthetic stream chunk requires ${transientBytes} bytes, exceeding the GPU storage buffer limit of ${deviceLimit} bytes`);

        const reservedBytes = overviewBytes + transientBytes;
        instance.generationReservedBytes += reservedBytes;

        const transientBuffer = instance.device.createBuffer({
            size: transientBytes,
            usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
        });
        const overviewBuffer = instance.device.createBuffer({ size: overviewBytes, usage: GPUBufferUsage.STORAGE });
        const paramsBuffer = instance.device.createBuffer({ size: 16, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
        const bindGroup = instance.device.createBindGroup({
            layout: instance.overviewPipeline.getBindGroupLayout(0),
            entries: [
                { binding: 0, resource: { buffer: transientBuffer } },
                { binding: 1, resource: { buffer: overviewBuffer } },
                { binding: 2, resource: { buffer: paramsBuffer } },
            ],
        });
        const worker = new Worker('js/chart.synthetic.worker.js');
        const requestId = ++instance.workerRequestId;
        let rangeMinimum = 0;
        let rangeMaximum = 0;
        let rangeHasValue = false;

        let rejectGeneration;
        const job = { worker, requestId, reservedBytes, reject: error => rejectGeneration?.(error) };
        instance.generationJobs.set(id, job);

        try {
            await new Promise((resolve, reject) => {
                rejectGeneration = reject;
                worker.onerror = event => reject(new Error(event.message));
                worker.onmessage = async event => {
                    if (event.data.requestId !== requestId)
                        return;

                    if (event.data.complete) {
                        resolve();
                        return;
                    }

                    if (instances.get(chartId) !== instance || instance.uploadGenerations.get(id) !== generation) {
                        reject(new Error(`Synthetic generation superseded for series ${id}`));
                        return;
                    }

                    try {
                        const values = event.data.values;
                        instance.device.queue.writeBuffer(transientBuffer, 0, values);
                        const chunkRange = await calculateSeriesRangeAsync(instance, transientBuffer, values.length);
                        if (chunkRange.hasValue) {
                            rangeMinimum = rangeHasValue ? Math.min(rangeMinimum, chunkRange.minimum) : chunkRange.minimum;
                            rangeMaximum = rangeHasValue ? Math.max(rangeMaximum, chunkRange.maximum) : chunkRange.maximum;
                            rangeHasValue = true;
                        }

                        instance.device.queue.writeBuffer(paramsBuffer, 0, new Uint32Array([
                            event.data.offset, values.length, Math.floor(event.data.offset / overviewBucketSize), 0,
                        ]));
                        const encoder = instance.device.createCommandEncoder();
                        const pass = encoder.beginComputePass();
                        pass.setPipeline(instance.overviewPipeline);
                        pass.setBindGroup(0, bindGroup);
                        pass.dispatchWorkgroups(Math.ceil(values.length / overviewBucketSize));
                        pass.end();
                        instance.device.queue.submit([encoder.finish()]);
                        await instance.device.queue.onSubmittedWorkDone();
                        worker.postMessage({ type: 'ack', requestId });
                    } catch (error) {
                        reject(error);
                    }
                };
                worker.postMessage({ type: 'stream', requestId, length, kind, chunkLength: syntheticStreamChunkLength });
            });

            if (instances.get(chartId) !== instance || instance.uploadGenerations.get(id) !== generation)
                throw new Error(`Synthetic generation superseded for series ${id}`);

            for (const [existingKey, existing] of instance.seriesBuffers) {
                if (existing.id === id) {
                    instance.persistentBytes -= existing.byteLength ?? 0;
                    destroySeriesBuffer(instance, existing);
                    instance.seriesBuffers.delete(existingKey);
                }
            }

            transientBuffer.destroy();
            const cached = {
                id, version, kind, length, buffer: overviewBuffer, pointBuffer: overviewBuffer,
                overviewLength, overviewBucketCount, byteLength: overviewBytes, dataMode: 1, synthetic: true, decimations: new Map(),
            };
            instance.seriesBuffers.set(getSeriesKey(id, version, length), cached);
            instance.persistentBytes += overviewBytes;
            return { hasValue: rangeHasValue, minimum: rangeMinimum, maximum: rangeMaximum };
        } catch (error) {
            transientBuffer.destroy();
            overviewBuffer.destroy();
            throw error;
        } finally {
            if (instance.generationJobs.get(id) === job)
                instance.generationJobs.delete(id);
            instance.generationReservedBytes -= reservedBytes;
            paramsBuffer.destroy();
            worker.terminate();
        }
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

    function trimDrawResources(instance, target, count) {
        const resources = instance.targetResources.get(target);

        if (!resources || resources.length <= count)
            return;

        for (const stale of resources.splice(count))
            stale.uniformBuffer.destroy();

        if (resources.length === 0)
            instance.targetResources.delete(target);
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
        const lastIndex = length - 1;
        const indexLeft = (valueOf(zoom, 'Left') ?? 0) * lastIndex;
        const indexRight = (valueOf(zoom, 'Right') ?? 1) * lastIndex;
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

    function getSyntheticOverviewZoom(payload, source, plot) {
        const zoom = valueOf(payload, 'Zoom') ?? {};
        const left = Math.max(0, Math.min(source.length - 1, (valueOf(zoom, 'Left') ?? 0) * (source.length - 1)));
        const right = Math.max(left, Math.min(source.length - 1, (valueOf(zoom, 'Right') ?? 1) * (source.length - 1)));
        const span = right - left;

        if (!Number.isFinite(span) || span <= 0)
            return null;

        const firstBucket = Math.max(0, Math.floor(left / overviewBucketSize) - 1);
        const lastBucket = Math.min(source.overviewBucketCount - 1, Math.ceil(right / overviewBucketSize));
        const first = firstBucket * reducedPointsPerBucket;
        const visibleLength = (lastBucket - firstBucket + 1) * reducedPointsPerBucket;

        if (visibleLength < 2)
            return null;

        return {
            first,
            segmentCount: visibleLength - 1,
            zoomedLeft: plot.plotLeft,
            dx: plot.plotWidth / (span / overviewBucketSize),
            xOrigin: left / overviewBucketSize,
        };
    }

    function getRenderBuffer(instance, source, zoomInfo, plot, encoder, target) {
        const visibleLength = zoomInfo.segmentCount + 1;
        const bucketCount = Math.min(maxDecimationBuckets, Math.max(2, Math.ceil(plot.plotWidth * decimationBucketsPerPixel)));

        if (visibleLength <= bucketCount * decimationFactor)
            return { seriesBuffer: source, zoomInfo };

        let decimation = source.decimations.get(target);
        const outputLength = getReducedOutputLength(bucketCount);
        const outputSize = outputLength * 2 * Float32Array.BYTES_PER_ELEMENT;

        if (!decimation || decimation.bucketCount !== bucketCount) {
            if (decimation) {
                decimation.outputBuffer.destroy();
                decimation.paramsBuffer.destroy();
                instance.auxiliaryBytes -= decimation.byteLength;
            }

            const allocationBytes = outputSize + 16;
            const outputBuffer = instance.device.createBuffer({
                size: outputSize,
                usage: GPUBufferUsage.STORAGE,
            });
            const paramsBuffer = instance.device.createBuffer({
                size: 16,
                usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
            });
            const bindGroup = instance.device.createBindGroup({
                layout: (source.dataMode === 1 ? instance.pointDecimationPipeline : instance.decimationPipeline).getBindGroupLayout(0),
                entries: [
                    { binding: 0, resource: { buffer: source.dataMode === 1 ? source.pointBuffer : source.buffer } },
                    { binding: 1, resource: { buffer: outputBuffer } },
                    { binding: 2, resource: { buffer: paramsBuffer } },
                ],
            });

            decimation = {
                bucketCount,
                outputBuffer,
                paramsBuffer,
                bindGroup,
                byteLength: allocationBytes,
                renderBuffer: {
                    buffer: source.buffer,
                    pointBuffer: outputBuffer,
                    length: outputLength,
                    dataMode: 1,
                },
            };
            source.decimations.set(target, decimation);
            instance.auxiliaryBytes += allocationBytes;
        }

        instance.device.queue.writeBuffer(
            decimation.paramsBuffer,
            0,
            new Uint32Array([zoomInfo.first, visibleLength, bucketCount, source.overviewLength ?? source.length]));

        const pass = encoder.beginComputePass();
        pass.setPipeline(source.dataMode === 1 ? instance.pointDecimationPipeline : instance.decimationPipeline);
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
                xOrigin: zoomInfo.xOrigin ?? 0,
            },
        };
    }

    function rawChunkKey(source, chunkIndex) {
        return `${source.id}:${source.version}:${chunkIndex}`;
    }

    function requestRawChunk(instance, source, chunkIndex, protectedKeys) {
        const key = rawChunkKey(source, chunkIndex);
        const cached = instance.rawChunks.get(key);

        if (cached) {
            cached.lastUsed = performance.now();
            return Promise.resolve(cached);
        }

        const pending = instance.rawRequests.get(key);
        if (pending)
            return pending.promise;

        const offset = chunkIndex * rawChunkLength;
        if (offset >= source.length)
            return Promise.resolve(null);

        const count = Math.min(rawChunkLength + (offset + rawChunkLength < source.length ? 1 : 0), source.length - offset);
        const byteLength = count * Float32Array.BYTES_PER_ELEMENT;
        evictRawChunks(instance, byteLength, protectedKeys);
        instance.rawReservedBytes += byteLength;

        const requestId = ++instance.workerRequestId;
        const worker = new Worker('js/chart.synthetic.worker.js');
        let resolveRequest;
        let rejectRequest;
        const promise = new Promise((resolve, reject) => {
            resolveRequest = resolve;
            rejectRequest = reject;
        });
        const request = { id: source.id, requestId, worker, promise, reject: rejectRequest, byteLength };
        instance.rawRequests.set(key, request);
        worker.onerror = event => {
            instance.rawRequests.delete(key);
            instance.rawReservedBytes -= byteLength;
            worker.terminate();
            rejectRequest(new Error(`Raw chunk ${chunkIndex} generation failed: ${event.message}`));
        };
        worker.onmessage = event => {
            if (event.data.requestId !== requestId)
                return;

            try {
                if (instances.get(source.chartId) !== instance || instance.uploadGenerations.get(source.id) !== source.generation)
                    throw new Error(`Raw chunk request superseded for series ${source.id}`);

                const values = event.data.values;
                const buffer = instance.device.createBuffer({
                    size: values.byteLength,
                    usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
                });
                instance.device.queue.writeBuffer(buffer, 0, values);
                const chunk = {
                    id: source.id, buffer, pointBuffer: buffer, dataMode: 0, decimations: new Map(),
                    offset, length: values.length, byteLength: values.byteLength, lastUsed: performance.now(),
                };
                instance.rawChunks.set(key, chunk);
                instance.rawReservedBytes -= byteLength;
                instance.rawCacheBytes += chunk.byteLength;
                instance.rawRequests.delete(key);
                evictRawChunks(instance, 0);
                worker.terminate();
                resolveRequest(chunk);
                rerenderLastPayloads(instance);
            } catch (error) {
                instance.rawRequests.delete(key);
                instance.rawReservedBytes -= byteLength;
                worker.terminate();
                rejectRequest(error);
            }
        };
        worker.postMessage({ type: 'raw', requestId, offset, count, kind: source.kind });
        return promise;
    }

    function rerenderLastPayloads(instance) {
        for (const [target, payload] of instance.lastPayloads) {
            instance.previewRenderKeys.delete(target);
            renderSeriesAsync(instance.chartId, payload).catch(error => console.error('[chart-webgpu] raw rerender failed', error));
        }
    }

    function getRawRenderItems(instance, source, payload, plot, encoder, target) {
        const zoom = valueOf(payload, 'Zoom') ?? {};
        const left = Math.max(0, (valueOf(zoom, 'Left') ?? 0) * (source.length - 1));
        const right = Math.min(source.length - 1, (valueOf(zoom, 'Right') ?? 1) * (source.length - 1));
        const span = right - left;

        if (!Number.isFinite(span) || span <= 0 || span > rawChunkLength * 2)
            return null;

        const firstChunk = Math.floor(left / rawChunkLength);
        const lastChunk = Math.floor(right / rawChunkLength);
        const protectedKeys = new Set();
        for (let index = firstChunk; index <= lastChunk; index++)
            protectedKeys.add(rawChunkKey(source, index));

        for (let index = firstChunk; index <= lastChunk; index++) {
            try {
                requestRawChunk(instance, source, index, protectedKeys).catch(error => console.error('[chart-webgpu] raw request failed', error));
            } catch (error) {
                console.error('[chart-webgpu] raw request failed', error);
            }
        }

        for (const index of [firstChunk - 1, lastChunk + 1]) {
            if (index < 0 || index >= Math.ceil(source.length / rawChunkLength))
                continue;

            try {
                requestRawChunk(instance, source, index, protectedKeys).catch(error => console.error('[chart-webgpu] raw prefetch failed', error));
            } catch (error) {
                console.error('[chart-webgpu] raw prefetch skipped', error);
            }
        }

        const items = [];
        for (let index = firstChunk; index <= lastChunk; index++) {
            const chunk = instance.rawChunks.get(rawChunkKey(source, index));
            if (!chunk)
                return null;

            chunk.lastUsed = performance.now();
            const localFirst = Math.max(0, Math.floor(left - chunk.offset));
            const localLast = Math.min(chunk.length - 1, Math.ceil(right - chunk.offset));
            if (localLast <= localFirst)
                continue;

            const zoomInfo = {
                first: localFirst,
                segmentCount: localLast - localFirst,
                zoomedLeft: plot.plotLeft + ((chunk.offset + localFirst - left) / span) * plot.plotWidth,
                dx: plot.plotWidth / span,
            };
            items.push(getRenderBuffer(instance, chunk, zoomInfo, plot, encoder, `${target}:${source.id}:${index}`));
        }

        return items.length ? items : null;
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
        floats[23] = zoomInfo.xOrigin ?? 0;

        instance.device.queue.writeBuffer(uniformBuffer, 0, data);
        return true;
    }

    async function beginSeriesUploadAsync(chartId, id, version, length) {
        const instance = await getInstance(chartId);

        if (!instance)
            throw new Error(`WebGPU instance unavailable for chart ${chartId}`);

        return createSeriesUpload(instance, id, version, length);
    }

    async function renderSeriesAsync(chartId, payload) {
        const instance = await getInstance(chartId);

        if (!instance)
            return;

        const { device, format, pipeline } = instance;
        const target = valueOf(payload, 'Target') ?? 'series';
        const canvas = document.getElementById(`${target}_${chartId}`);

        if (!canvas) {
            releaseCanvasContext(instance, target);
            return;
        }

        const context = getCanvasContext(instance, target, canvas);
        const { width, height, dpr } = ensureCanvasSize(canvas);
        const isPreview = valueOf(payload, 'Preview') ?? false;
        instance.lastPayloads.set(target, payload);
        let previewRenderKey = null;

        if (isPreview) {
            previewRenderKey = getPreviewRenderKey(instance, payload, width, height);

            if (instance.previewRenderKeys.get(target) === previewRenderKey)
                return;
        }

        const plot = getPlot(payload, width, height);

        const encoder = device.createCommandEncoder();
        const renderItems = [];
        let drawResourceCount = 0;

        if (plot) {
            const seriesList = valueOf(payload, 'Series') ?? [];

            for (const series of seriesList) {
                const cached = getSeriesBuffer(instance, series);

                if (!cached)
                    continue;

                cached.chartId = chartId;
                cached.generation = instance.uploadGenerations.get(cached.id);
                if (cached.synthetic) {
                    const rawItems = getRawRenderItems(instance, cached, payload, plot, encoder, target);
                    if (rawItems) {
                        for (const rawItem of rawItems)
                            renderItems.push({ series, ...rawItem });
                        continue;
                    }
                }

                const zoomInfo = cached.synthetic
                    ? getSyntheticOverviewZoom(payload, cached, plot)
                    : getZoomInfo(payload, cached.length, plot);

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

            for (const { series, seriesBuffer, zoomInfo } of renderItems) {
                const fillResources = getDrawResources(instance, seriesBuffer, drawResourceCount++, target);

                if (writeUniforms(instance, fillResources.uniformBuffer, seriesBuffer, payload, series, plot, zoomInfo, width, height, dpr, 0)) {
                    pass.setBindGroup(0, fillResources.bindGroup);
                    pass.draw(zoomInfo.segmentCount * fillVerticesPerSegment);
                }
            }

            for (const { series, seriesBuffer, zoomInfo } of renderItems) {
                const lineResources = getDrawResources(instance, seriesBuffer, drawResourceCount++, target);

                if (writeUniforms(instance, lineResources.uniformBuffer, seriesBuffer, payload, series, plot, zoomInfo, width, height, dpr, 1)) {
                    pass.setBindGroup(0, lineResources.bindGroup);
                    pass.draw(zoomInfo.segmentCount * lineVerticesPerSegment);
                }
            }
        }

        pass.end();
        device.queue.submit([encoder.finish()]);
        trimDrawResources(instance, target, drawResourceCount);

        if (previewRenderKey !== null)
            instance.previewRenderKeys.set(target, previewRenderKey);

    }

    window.nexus ??= {};
    window.nexus.chartWebGpu = {
        initialize(chartId, dotNetHelper) {
            dotNetHelpers.set(chartId, dotNetHelper);

            const failure = failureStates.get(chartId);
            if (failure) {
                dotNetHelper.invokeMethodAsync('WebGpuFailed', failure.title, failure.message)
                    .catch(error => console.error('[chart-webgpu] failure callback failed', error));
                return;
            }

            getInstance(chartId).catch(error => console.error('[chart-webgpu] initialization failed', error));
        },
        setCacheBudget(chartId, bytes) {
            if (!Number.isSafeInteger(bytes) || bytes < 0)
                throw new Error(`Cache budget must be a non-negative safe integer (received ${bytes})`);

            const instance = instances.get(chartId);
            if (instance) {
                instance.cacheBudget = bytes;
                evictRawChunks(instance, 0);
            }

            configuredCacheBudgets.set(chartId, bytes);
        },
        synchronizeSeries(chartId, activeIds) {
            const instance = instances.get(chartId);
            if (instance)
                synchronizeSeries(instance, activeIds);
        },
        beginSeriesUpload(chartId, id, version, length) {
            return beginSeriesUploadAsync(chartId, id, version, length);
        },
        appendSeriesUpload(chartId, token, byteOffset, streamReference) {
            return appendSeriesUploadAsync(chartId, token, byteOffset, streamReference);
        },
        completeSeriesUpload(chartId, token) {
            return completeSeriesUploadAsync(chartId, token);
        },
        abortSeriesUpload(chartId, token) {
            abortSeriesUpload(chartId, token);
        },
        generateSyntheticSeries(chartId, id, version, length, kind) {
            return generateSyntheticSeriesAsync(chartId, id, version, length, kind);
        },
        renderSeries(chartId, payload) {
            renderSeriesAsync(chartId, payload).catch(error => console.error('[chart-webgpu] render failed', error));
        },
        async retry(chartId) {
            advanceLifecycleEpoch(chartId);
            pendingInstances.delete(chartId);

            const instance = instances.get(chartId);
            if (instance) {
                instances.delete(chartId);
                destroyInstance(instance, `Chart ${chartId} WebGPU retry`);
            }

            failureStates.delete(chartId);
            return (await getInstance(chartId)) !== null;
        },
        dispose(chartId) {
            advanceLifecycleEpoch(chartId);
            pendingInstances.delete(chartId);

            const instance = instances.get(chartId);
            instances.delete(chartId);

            if (instance)
                destroyInstance(instance, `Chart ${chartId} was disposed`);

            configuredCacheBudgets.delete(chartId);
            failureStates.delete(chartId);
            dotNetHelpers.delete(chartId);
        },
    };

    if (globalThis.__nexusChartWebGpuTestHooks) {
        Object.assign(globalThis.__nexusChartWebGpuTestHooks, {
            instances,
            pendingInstances,
            lifecycleEpochs,
            failureStates,
            getSharedGpu,
            getInstance,
            evictRawChunks,
            trimDrawResources,
            colorOf,
            getCanvasContext,
            releaseCanvasContext,
            getReducedOutputLength,
            reducedPointsPerBucket,
        });
    }
})();
