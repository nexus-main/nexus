window.nexus={},nexus.chart={},nexus.chart.charts={},nexus.chart.resize=function(l,G,k,$,R,M){let O=document.getElementById(`${G}_${l}`);O.style.left=`${k*100}%`,O.style.top=`${$*100}%`,O.style.width=`${(R-k)*100}%`,O.style.height=`${(M-$)*100}%`},nexus.chart.setTextContent=function(l,G,k){let $=document.getElementById(`${G}_${l}`);$.textContent=k},nexus.chart.translate=function(l,G,k,$){let R=document.getElementById(`${G}_${l}`);R.style.removeProperty("display"),R.style.left=`${k*100}%`,R.style.top=`${$*100}%`},nexus.chart.hide=function(l,G){let k=document.getElementById(`${G}_${l}`);k.style.display="none"},nexus.chart.updateAuxiliary=function(l,G,k,$,R){nexus.chart.setTextContent(l,"value_datetime",$),nexus.chart.translate(l,"crosshairs-x",0,k),nexus.chart.translate(l,"crosshairs-y",G,0);for(const M of R){const O=M.id??M.Id,L=M.visible??M.Visible,V=M.text??M.Text;L?nexus.chart.translate(l,`pointer_${O}`,M.x??M.X,M.y??M.Y):nexus.chart.hide(l,`pointer_${O}`),nexus.chart.setTextContent(l,`value_${O}`,V)}},nexus.chart.clearAuxiliary=function(l){for(const k of["crosshairs-x","crosshairs-y"]){const $=document.getElementById(`${k}_${l}`);$&&($.style.display="none")}const G=document.getElementById(`chart_${l}`);for(const k of G?.querySelectorAll(".pointer")??[])k.style.display="none";for(const k of G?.parentElement?.querySelectorAll('[id^="value_"]')??[])k.textContent="--"},nexus.chart.toRelative=function(l,G,k){let R=document.getElementById(`overlay_${l}`).getBoundingClientRect(),M=(G-R.left)/R.width,O=(k-R.top)/R.height;return M=Math.max(0,M),M=Math.min(1,M),O=Math.max(0,O),O=Math.min(1,O),{x:M,y:O}},nexus.chart.initInteractions=function(l,G){const k=document.getElementById(`overlay_${l}`),$=document.getElementById(`selection_${l}`),R=[],M=(i,s,g)=>Math.max(s,Math.min(i,g)),O=(i,s)=>{const g=typeof i=="number"?i:Number.parseFloat(i);return Number.isFinite(g)?g:s},L=(i,s)=>{const g=s-i;return g>=1?[0,1]:i<0?[0,g]:s>1?[1-g,1]:[i,s]},V=(i,s,g,h,w)=>{i=M(O(i,0),0,1),s=M(O(s,1),i,1),g=M(O(g,.5),0,1),h=O(h,1),w=M(O(w,Number.EPSILON),Number.MIN_VALUE,1);let a=s-i;if(a<w){const r=M((i+s)/2,0,1);i=M(r-w/2,0,1-w),s=i+w,a=w}const m=i+g*a,E=Number.EPSILON*Math.max(1,Math.abs(i),Math.abs(s)),y=a*h,e=M(h>1?Math.max(y,a+E):y,w,1);return L(m-g*e,m+(1-g)*e)};let re=!1,Z=null,ne=!1,J=null,Q=null,ee=!1;function Y(i,s){Z={method:i,values:s},!re&&(re=!0,requestAnimationFrame(async()=>{const g=Z;Z=null;try{!ee&&g&&(await G.invokeMethodAsync(g.method,...g.values),!ee&&Q&&C(...Q))}catch(h){console.error("[chart] zoom update failed",h)}finally{re=!1,!ee&&Z&&Y(Z.method,Z.values)}}))}function F(i,s,g,h){i.addEventListener(s,g,h),R.push(()=>i.removeEventListener(s,g,h))}function C(i,s){J=[i,s],!ne&&(ne=!0,requestAnimationFrame(async()=>{const g=J;J=null;try{!ee&&g&&await G.invokeMethodAsync("PointerMoved",...g)}catch(h){console.error("[chart] pointer update failed",h)}finally{ne=!1,!ee&&J&&C(...J)}}))}if(k&&$){let i=null,s=null;const g=20,h=(a,m,E,y,e)=>{const r=Math.abs(E-a)*e.width,n=Math.abs(y-m)*e.height,o=r>n&&n<=g,x=n>r&&r<=g;return{left:o||!x?Math.min(a,E):0,right:o||!x?Math.max(a,E):1,top:x||!o?Math.min(m,y):0,bottom:x||!o?Math.max(m,y):1}};F(k,"mousemove",a=>{const m=k.getBoundingClientRect();m.width<=0||m.height<=0||(Q=[M((a.clientX-m.left)/m.width,0,1),M((a.clientY-m.top)/m.height,0,1)],C(...Q))}),F(k,"mouseleave",()=>{Q=null,J=null,ee||nexus.chart.clearAuxiliary(l)}),F(k,"wheel",a=>{a.cancelable&&a.preventDefault();const m=k.getBoundingClientRect(),E=M((a.clientX-m.left)/m.width,0,1),y=M((a.clientY-m.top)/m.height,0,1);if(a.shiftKey)Y("WheelZoom",[E,y,a.deltaY,!0]);else{const e=V(k.dataset.zoomLeft,k.dataset.zoomRight,E,a.deltaY<0?.85:1.1764705882352942,k.dataset.minimumHorizontalZoom);let r=M(O(k.dataset.zoomTop,0),0,1),n=M(O(k.dataset.zoomBottom,1),r,1);a.deltaY>0&&e[0]===0&&e[1]===1&&([r,n]=V(r,n,y,1/.85,1e-6),r=Math.fround(r),n=Math.fround(n)),k.dataset.zoomLeft=e[0].toString(),k.dataset.zoomRight=e[1].toString(),k.dataset.zoomTop=r.toString(),k.dataset.zoomBottom=n.toString(),Y("SetViewport",[e[0],r,e[1],n])}},{passive:!1}),F(k,"pointerdown",a=>{if(a.button!==0&&a.button!==1)return;a.cancelable&&a.preventDefault();const m=k.getBoundingClientRect(),E=M((a.clientX-m.left)/m.width,0,1),y=M((a.clientY-m.top)/m.height,0,1);if(a.pointerType==="touch"||a.pointerType==="pen"){const e=O(a.timeStamp,Date.now()),r=s;if(s={time:e,x:a.clientX,y:a.clientY},r&&e-r.time<=350&&Math.hypot(a.clientX-r.x,a.clientY-r.y)<=24){s=null,k.dataset.zoomLeft="0",k.dataset.zoomRight="1",k.dataset.zoomTop="0",k.dataset.zoomBottom="1",$.style.display="none",i=null,Y("SetViewport",[0,0,1,1]);return}}k.setPointerCapture(a.pointerId),i={pointerId:a.pointerId,rect:m,startX:E,startY:y,currentX:0,currentY:0,pan:a.button===1||a.altKey||a.ctrlKey||a.metaKey,zoom:{left:parseFloat(k.dataset.zoomLeft),top:parseFloat(k.dataset.zoomTop),right:parseFloat(k.dataset.zoomRight),bottom:parseFloat(k.dataset.zoomBottom)}},i.currentX=i.startX,i.currentY=i.startY}),F(k,"pointermove",a=>{if(!i||i.pointerId!==a.pointerId)return;const m=M((a.clientX-i.rect.left)/i.rect.width,0,1),E=M((a.clientY-i.rect.top)/i.rect.height,0,1);if(i.currentX=m,i.currentY=E,i.pan){const o=i.zoom.right-i.zoom.left,x=i.zoom.bottom-i.zoom.top,B=M(i.zoom.left-(m-i.startX)*o,0,1-o),b=M(i.zoom.top-(E-i.startY)*x,0,1-x);Y("SetViewport",[B,b,B+o,b+x]);return}const{left:y,top:e,right:r,bottom:n}=h(i.startX,i.startY,m,E,i.rect);Object.assign($.style,{display:"block",left:`${y*100}%`,top:`${e*100}%`,width:`${(r-y)*100}%`,height:`${(n-e)*100}%`})});const w=a=>{if(!i||i.pointerId!==a.pointerId)return;$.style.display="none";const m=i;if(i=null,m.pan)return;const E=Math.abs(m.currentX-m.startX)*m.rect.width,y=Math.abs(m.currentY-m.startY)*m.rect.height;if(Math.hypot(E,y)<6)return;const{left:e,top:r,right:n,bottom:o}=h(m.startX,m.startY,m.currentX,m.currentY,m.rect);Y("DragZoom",[e,r,n,o])};F(k,"pointerup",w),F(k,"pointercancel",w)}function U(i){const s=document.getElementById(`${i}-track_${l}`),g=document.getElementById(`${i}-window_${l}`),h=document.getElementById(`${i}-handle-left_${l}`),w=document.getElementById(`${i}-handle-right_${l}`);if(!s||!g||!h||!w)return;let a=null;function m(y,e){e.button===0&&(e.cancelable&&e.preventDefault(),e.stopPropagation(),s.setPointerCapture(e.pointerId),a={mode:y,pointerId:e.pointerId,rect:s.getBoundingClientRect(),startX:e.clientX,startLeft:parseFloat(g.dataset.left),startRight:parseFloat(g.dataset.right),domainLeft:parseFloat(s.dataset.domainLeft),domainRight:parseFloat(s.dataset.domainRight)})}F(h,"pointerdown",y=>m("left",y)),F(w,"pointerdown",y=>m("right",y)),F(g,"pointerdown",y=>m("pan",y)),F(s,"pointerdown",y=>{if(y.target!==s&&y.target.tagName!=="CANVAS")return;const e=s.getBoundingClientRect(),r=parseFloat(s.dataset.domainLeft),n=parseFloat(s.dataset.domainRight),o=r+M((y.clientX-e.left)/e.width,0,1)*(n-r),x=parseFloat(g.dataset.right)-parseFloat(g.dataset.left),B=M(o-x/2,0,1-x);Y("NavigatorZoom",[B,B+x])}),F(s,"pointermove",y=>{if(!a||a.pointerId!==y.pointerId)return;y.cancelable&&y.preventDefault();const e=a.domainRight-a.domainLeft,r=(y.clientX-a.startX)/a.rect.width*e,n=Math.max(Number.EPSILON,e/Math.max(1,a.rect.width*4));let o,x;if(a.mode==="left")o=M(a.startLeft+r,0,a.startRight-n),x=a.startRight;else if(a.mode==="right")o=a.startLeft,x=M(a.startRight+r,a.startLeft+n,1);else{const B=a.startRight-a.startLeft;o=M(a.startLeft+r,0,1-B),x=o+B}Y("NavigatorZoom",[o,x])});const E=y=>{a?.pointerId===y.pointerId&&(a=null)};F(s,"pointerup",E),F(s,"pointercancel",E),F(s,"wheel",y=>{y.cancelable&&y.preventDefault();const e=s.getBoundingClientRect(),r=parseFloat(s.dataset.domainLeft),n=parseFloat(s.dataset.domainRight),o=r+M((y.clientX-e.left)/e.width,0,1)*(n-r),x=parseFloat(g.dataset.left),B=parseFloat(g.dataset.right),b=y.deltaY<0?.9:1.1111111111111112;Y("NavigatorZoom",L(o-(o-x)*b,o+(B-o)*b))},{passive:!1}),F(g,"keydown",y=>{if(y.key!=="ArrowLeft"&&y.key!=="ArrowRight")return;y.cancelable&&y.preventDefault();const e=y.key==="ArrowLeft"?-1:1,r=parseFloat(g.dataset.left),n=parseFloat(g.dataset.right),o=n-r,x=o*(y.shiftKey?.1:.02)*e;if(y.altKey){const B=M(n+x,r+Number.EPSILON,1);Y("NavigatorZoom",[r,B])}else{const B=M(r+x,0,1-o);Y("NavigatorZoom",[B,B+o])}})}U("navigator"),U("navigator-detail"),nexus.chart.charts[l]={dispose:()=>{ee=!0,J=null,Z=null,R.forEach(i=>i())}}},nexus.chart.dispose=function(l){nexus.chart.charts[l]?.dispose(),delete nexus.chart.charts[l]},(function(){const l=window.__nexusChartWebGpu={},G=96,k=6,$=18,R=64,M=4,O=2,L=8192,V=256,re=1024,Z=512*1024*1024,ne=256,J=5,Q=4*1024*1024,ee=1024*1024,Y=`
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
`;function F(s){return`
fn projectBucket(start: u32, end: u32, minIndex: u32, maxIndex: u32,
    gapIndex: u32, invalidCount: u32, outIndex: u32, origin: u32, scale: f32) {
    // Keep NaN construction at runtime; WGSL constant evaluation rejects NaNs.
    let nan = bitcast<f32>(0x7fc00000u | (start & 1u));
    var indices = array<u32, 5>(start, end - 1u, minIndex, maxIndex, gapIndex);
    // Compare against finite count to avoid doubling and overflowing u32.
    let bucketCount = end - start;
    let mostlyInvalid = invalidCount >= bucketCount - invalidCount;
    var count = select(4u, 5u, invalidCount != 0u);
    if (mostlyInvalid) {
        indices[0] = gapIndex;
        count = 1u;
    }
    for (var i = 1u; i < count; i++) {
        let index = indices[i];
        var j = i;
        while (j > 0u) {
            if (indices[j - 1u] <= index) { break; }
            indices[j] = indices[j - 1u];
            j--;
        }
        indices[j] = index;
    }
    var written = 0u;
    var previous = 0xffffffffu;
    var point = vec2f(0.0, nan);
    for (var i = 0u; i < count; i++) {
        let index = indices[i];
        if (index == previous) { continue; }
        previous = index;
        ${s?"point = source[index];":"point = vec2f(f32(index - origin) * scale, source[index]);"}
        if (isNonFinite(point.y) || mostlyInvalid) { point.y = nan; }
        output[outIndex + written] = point;
        written++;
    }
    while (written < ${J}u) {
        output[outIndex + written] = point;
        written++;
    }
}
`}const C=`
struct Params {
    first: u32,
    visibleLength: u32,
    bucketCount: u32,
    sourceLength: u32,
};
@group(0) @binding(0) var<storage, read> source: array<vec2f>;
@group(0) @binding(1) var<storage, read_write> output: array<vec2f>;
@group(0) @binding(2) var<uniform> params: Params;
var<workgroup> minimums: array<vec2f, ${R}>;
var<workgroup> maximums: array<vec2f, ${R}>;
var<workgroup> valid: array<u32, ${R}>;
var<workgroup> nanSeen: array<u32, ${R}>;
var<workgroup> minimumIndices: array<u32, ${R}>;
var<workgroup> maximumIndices: array<u32, ${R}>;
var<workgroup> nanIndices: array<u32, ${R}>;
var<workgroup> invalidCounts: array<u32, ${R}>;
fn isNonFinite(x: f32) -> bool { let b = bitcast<u32>(x); return (b & 0x7f800000u) == 0x7f800000u; }
${F(!0)}
@compute @workgroup_size(${R})
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
    var invalidCount = 0u;
    var index = start + lane;
    while (index < end) {
        let point = source[index];
        if (isNonFinite(point.y)) {
            if (hasNan == 0u || index < nanIndex) { nanIndex = index; }
            hasNan = 1u;
            invalidCount++;
        }
        else {
            if (hasValue == 0u || point.y < minimum.y) { minimum = point; minimumIndex = index; }
            if (hasValue == 0u || point.y > maximum.y) { maximum = point; maximumIndex = index; }
            hasValue = 1u;
        }
        index += ${R}u;
    }
    minimums[lane] = minimum; maximums[lane] = maximum; valid[lane] = hasValue; nanSeen[lane] = hasNan;
    minimumIndices[lane] = minimumIndex; maximumIndices[lane] = maximumIndex; nanIndices[lane] = nanIndex;
    invalidCounts[lane] = invalidCount;
    workgroupBarrier();
    var stride = ${R/2}u;
    while (stride > 0u) {
        if (lane < stride) {
            let other = lane + stride;
            if (valid[other] != 0u) {
                if (valid[lane] == 0u || minimums[other].y < minimums[lane].y || (minimums[other].y == minimums[lane].y && minimumIndices[other] < minimumIndices[lane])) { minimums[lane] = minimums[other]; minimumIndices[lane] = minimumIndices[other]; }
                if (valid[lane] == 0u || maximums[other].y > maximums[lane].y || (maximums[other].y == maximums[lane].y && maximumIndices[other] < maximumIndices[lane])) { maximums[lane] = maximums[other]; maximumIndices[lane] = maximumIndices[other]; }
                valid[lane] = 1u;
            }
            if (nanSeen[other] != 0u && (nanSeen[lane] == 0u || nanIndices[other] < nanIndices[lane])) { nanIndices[lane] = nanIndices[other]; }
            nanSeen[lane] |= nanSeen[other];
            invalidCounts[lane] += invalidCounts[other];
        }
        workgroupBarrier(); stride /= 2u;
    }
    if (lane == 0u) {
        projectBucket(start, end, minimumIndices[0], maximumIndices[0], nanIndices[0],
            invalidCounts[0], bucket * ${J}u, 0u, 1.0);
    }
}
`,U=`
struct Params {
    first: u32,
    visibleLength: u32,
    bucketCount: u32,
    sourceLength: u32,
};

@group(0) @binding(0) var<storage, read> source: array<f32>;
@group(0) @binding(1) var<storage, read_write> output: array<vec2f>;
@group(0) @binding(2) var<uniform> params: Params;

var<workgroup> minimums: array<f32, ${R}>;
var<workgroup> maximums: array<f32, ${R}>;
var<workgroup> minimumIndices: array<u32, ${R}>;
var<workgroup> maximumIndices: array<u32, ${R}>;
var<workgroup> valid: array<u32, ${R}>;
var<workgroup> nanSeen: array<u32, ${R}>;
var<workgroup> nanIndices: array<u32, ${R}>;
var<workgroup> invalidCounts: array<u32, ${R}>;

fn isNonFinite(x: f32) -> bool {
    let bits = bitcast<u32>(x);
    return (bits & 0x7f800000u) == 0x7f800000u;
}

${F(!1)}

@compute @workgroup_size(${R})
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
    var invalidCount = 0u;
    var index = start + lane;

    while (index < end) {
        let value = source[index];

        if (isNonFinite(value)) {
            if (hasNan == 0u || index < nanIndex) {
                nanIndex = index;
            }

            hasNan = 1u;
            invalidCount++;
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

        index += ${R}u;
    }

    minimums[lane] = minimum;
    maximums[lane] = maximum;
    minimumIndices[lane] = minimumIndex;
    maximumIndices[lane] = maximumIndex;
    valid[lane] = hasValue;
    nanSeen[lane] = hasNan;
    nanIndices[lane] = nanIndex;
    invalidCounts[lane] = invalidCount;
    workgroupBarrier();

    var stride = ${R/2}u;
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
            invalidCounts[lane] += invalidCounts[lane + stride];
        }

        workgroupBarrier();
        stride /= 2u;
    }

    if (lane == 0u) {
        projectBucket(start, end, minimumIndices[0], maximumIndices[0], nanIndices[0],
            invalidCounts[0], bucket * ${J}u, params.first, 1.0);
    }
}
`,i=`
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

var<workgroup> minimums: array<f32, ${V}>;
var<workgroup> maximums: array<f32, ${V}>;
var<workgroup> valid: array<u32, ${V}>;

fn isFiniteValue(x: f32) -> bool {
    let bits = bitcast<u32>(x);
    return (bits & 0x7f800000u) != 0x7f800000u;
}

@compute @workgroup_size(${V})
fn reduceRange(
    @builtin(workgroup_id) workgroupId: vec3u,
    @builtin(local_invocation_id) localId: vec3u) {
    let group = workgroupId.x;
    let lane = localId.x;
    let invocationCount = params.workgroupCount * ${V}u;
    var index = group * ${V}u + lane;
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

    var stride = ${V/2}u;
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
`;Object.assign(l,{uniformBufferSize:G,fillVerticesPerSegment:k,lineVerticesPerSegment:$,decimationWorkgroupSize:R,decimationFactor:M,decimationBucketsPerPixel:O,maxDecimationBuckets:L,rangeWorkgroupSize:V,maxRangeWorkgroups:re,defaultCacheBudget:Z,overviewBucketSize:ne,reducedPointsPerBucket:J,streamChunkLength:Q,rawChunkLength:ee,shader:Y,pointDecimationShader:C,decimationShader:U,rangeShader:i})})(),(function(){const l=window.__nexusChartWebGpu,{shader:G,decimationShader:k,rangeShader:$,pointDecimationShader:R,defaultCacheBudget:M,reducedPointsPerBucket:O}=l,L=new Map,V=new Map,re=new Map,Z=new Map,ne=new Map,J=new Map;let Q=null,ee=null,Y=0;function F(t,d){if(!t)return;const p=d.charAt(0).toLowerCase()+d.slice(1);return t[d]??t[p]}function C(t){const d=F(t,"Color")??{};return[(F(d,"Red")??0)/255,(F(d,"Green")??0)/255,(F(d,"Blue")??0)/255,(F(d,"Alpha")??255)/255]}function U(t){const d=window.devicePixelRatio||1,p=Math.max(1,Math.round(t.clientWidth*d)),P=Math.max(1,Math.round(t.clientHeight*d));return(t.width!==p||t.height!==P)&&(t.width=p,t.height=P),{width:p,height:P,dpr:d}}function i(t,d,p){const P=t.canvasContexts.get(d);if(P?.canvas===p)return P.context;P?.context.unconfigure?.(),t.previewRenderKeys.delete(d);const _=p.getContext("webgpu");if(!_)throw new Error("The browser could not create a WebGPU canvas context.");return _.configure({device:t.device,format:t.format,alphaMode:"premultiplied"}),t.canvasContexts.set(d,{canvas:p,context:_}),_}function s(t,d){t.canvasContexts.get(d)?.context.unconfigure?.(),t.canvasContexts.delete(d),t.previewRenderKeys.delete(d)}function g(t){return t*O}function h(t,d){!d||d.__nexusDestroyed||(d.__nexusDestroyed=!0,t.ownedGpuBytes-=d.__nexusByteLength??0,d.destroy())}function w(t,d,p=new Set,P=t.cacheBudget){if(l.evictRawChunks?.(t,d,p,P),t.ownedGpuBytes+t.rawReservedBytes+d>P)throw new Error(`Chart GPU memory budget (${P} bytes) cannot fit a ${d}-byte allocation`)}function a(t,d,p){w(t,d.size,p);const P=t.device.createBuffer(d);return P.__nexusByteLength=d.size,P.__nexusDestroyed=!1,t.ownedGpuBytes+=d.size,P}function m(t){return re.get(t)??0}function E(t){const d=m(t)+1;return re.set(t,d),d}function y(t){const d=new Error(t);return d.webGpuCancelled=!0,d}function e(t){return t?.webGpuCancelled===!0}function r(t,d,p){const P=Z.get(t);if(P?.title===d&&P?.message===p)return;Z.set(t,{title:d,message:p}),ne.get(t)?.invokeMethodAsync("WebGpuFailed",d,p).catch(j=>console.error("[chart-webgpu] failure callback failed",j))}function n(t,d,p,P){if(m(t)!==d||Z.has(t))return;E(t),V.delete(t);const _=L.get(t);L.delete(t),_&&o(_,`${p}: ${P}`),x(),r(t,p,P)}function o(t,d){if(!(!t||t.disposed)){t.disposed=!0,l.invalidateChartRenders?.(t.chartId);for(const p of t.rawRequests.values())t.rawReservedBytes-=p.byteLength,p.reject(y(d));t.rawRequests.clear();for(const[p,P]of t.rawChunks)l.destroyRawChunk(t,p,P);for(const p of t.seriesBuffers.values())l.destroySeriesBuffer(t,p);t.seriesBuffers.clear();for(const p of t.chunkedUploadSessions.values())l.destroyChunkedUpload(t,p);t.chunkedUploadSessions.clear();for(const p of t.targetResources.values())for(const P of p)h(t,P.uniformBuffer);t.targetResources.clear();for(const{context:p}of t.canvasContexts.values())p.unconfigure?.();t.canvasContexts.clear(),t.lastPayloads.clear(),t.previewRenderKeys.clear()}}function x(){if(L.size>0||V.size>0||(Y++,!Q))return;const t=Q;Q=null,t.alive=!1,t.device.destroy()}async function B(){if(Q)return Q;if(ee)return ee;const t=++Y;return ee=Promise.resolve().then(async()=>{if(!navigator.gpu)throw new Error("WebGPU is not available. Use a current WebGPU-capable browser and ensure hardware acceleration is enabled.");const d=await navigator.gpu.requestAdapter();if(!d)throw new Error("No compatible GPU adapter was found. Ensure hardware acceleration is enabled, then retry.");const p=await d.requestDevice({requiredLimits:{maxBufferSize:d.limits.maxBufferSize,maxStorageBufferBindingSize:d.limits.maxStorageBufferBindingSize,maxStorageBuffersPerShaderStage:5}});let P;try{if(t!==Y)throw new Error("WebGPU initialization was superseded.");const _=navigator.gpu.getPreferredCanvasFormat(),j=p.createShaderModule({code:G}),W=p.createShaderModule({code:k}),q=p.createShaderModule({code:$}),D=p.createShaderModule({code:R});P={generation:t,alive:!0,device:p,format:_,pipeline:p.createRenderPipeline({layout:"auto",vertex:{module:j,entryPoint:"vertexMain"},fragment:{module:j,entryPoint:"fragmentMain",targets:[{format:_,blend:{color:{srcFactor:"src-alpha",dstFactor:"one-minus-src-alpha",operation:"add"},alpha:{srcFactor:"one",dstFactor:"one-minus-src-alpha",operation:"add"}}}]},primitive:{topology:"triangle-list"}}),decimationPipeline:p.createComputePipeline({layout:"auto",compute:{module:W,entryPoint:"decimate"}}),rangePipeline:p.createComputePipeline({layout:"auto",compute:{module:q,entryPoint:"reduceRange"}}),pointDecimationPipeline:p.createComputePipeline({layout:"auto",compute:{module:D,entryPoint:"decimatePoints"}})}}catch(_){throw p.destroy(),_}return p.lost.then(_=>{P.alive=!1,Q===P&&(Q=null,Y++);const j=_.message?` ${_.message}`:"";for(const[W,q]of[...L])q.gpuGeneration===P.generation&&(E(W),L.delete(W),o(q,`WebGPU device lost for chart ${W}`),r(W,"GPU connection lost",`The browser lost access to the GPU.${j} Retry the chart to recreate its GPU resources.`))}).catch(_=>console.error("[chart-webgpu] device loss handler failed",_)),Q=P,P}).finally(()=>{ee=null}),ee}async function b(t){if(!ne.has(t))throw y(`Chart ${t} is no longer active`);let d=L.get(t);if(d)return d;const p=Z.get(t);if(p)throw new Error(p.message);let P=V.get(t);if(P)return P.promise;const _=m(t),j={epoch:_,promise:null};return j.promise=Promise.resolve().then(async()=>{try{if(m(t)!==_)return null;if(!document.getElementById(`series_${t}`))throw new Error("The chart canvas is unavailable.");const q=await B();if(m(t)!==_)return null;if(!q.alive||Q!==q||q.generation!==Y)throw new Error("The WebGPU device was lost during chart initialization.");return d={chartId:t,...q,gpuGeneration:q.generation,seriesBuffers:new Map,chunkedUploadSessions:new Map,uploadToken:0,targetResources:new Map,canvasContexts:new Map,previewRenderKeys:new Map,uploadGenerations:new Map,cacheBudget:J.get(t)??M,ownedGpuBytes:0,rawReservedBytes:0,rawChunks:new Map,rawRequests:new Map,workerRequestId:0,lastPayloads:new Map,disposed:!1},m(t)!==_?(o(d,`Chart ${t} initialization was superseded`),null):(L.set(t,d),d)}catch(W){if(!Z.has(t)&&m(t)===_){const q=!navigator.gpu||W?.message?.startsWith("No compatible GPU adapter");r(t,q?"WebGPU unavailable":"WebGPU initialization failed",q?W.message:`The chart could not initialize WebGPU: ${W?.message??W}. Check browser hardware acceleration, then retry.`)}throw W}finally{V.get(t)===j&&V.delete(t),!ne.has(t)&&!L.has(t)&&re.delete(t)}}),V.set(t,j),j.promise}Object.assign(l,{instances:L,pendingInstances:V,lifecycleEpochs:re,failureStates:Z,dotNetHelpers:ne,configuredCacheBudgets:J,valueOf:F,colorOf:C,ensureCanvasSize:U,getCanvasContext:i,releaseCanvasContext:s,getReducedOutputLength:g,createTrackedBuffer:a,destroyTrackedBuffer:h,ensureGpuCapacity:w,getLifecycleEpoch:m,advanceLifecycleEpoch:E,cancellationError:y,isCancellationError:e,reportFailure:r,reportRuntimeFailure:n,destroyInstance:o,releaseSharedGpuIfUnused:x,getSharedGpu:B,getInstance:b})})(),(function(){const l=window.__nexusChartWebGpu,{instances:G,dotNetHelpers:k,valueOf:$,rawChunkLength:R,rangeWorkgroupSize:M,maxRangeWorkgroups:O}=l;function L(e,r,n){return`${e}:${r}:${n}`}function V(e,r){for(const n of r.pages??[])n.buffer!==r.buffer&&l.destroyTrackedBuffer(e,n.buffer);l.destroyTrackedBuffer(e,r.buffer),r.pointBuffer!==r.buffer&&l.destroyTrackedBuffer(e,r.pointBuffer);for(const n of r.decimations?.values()??[])l.destroyTrackedBuffer(e,n.outputBuffer),l.destroyTrackedBuffer(e,n.paramsBuffer)}function re(e,r,n){V(e,n),e.rawChunks.delete(r)}function Z(e,r,n){e.rawRequests.get(r)===n&&e.rawRequests.delete(r),n.reservationActive&&(e.rawReservedBytes-=n.byteLength,n.reservationActive=!1),l.destroyTrackedBuffer(e,n.buffer)}function ne(e,r,n,o,x){n.buffer=null;const B={id:n.id,buffer:o,pointBuffer:o,dataMode:0,decimations:new Map,offset:n.offset,length:x,byteLength:n.byteLength,lastUsed:performance.now()};e.rawChunks.set(r,B),Z(e,r,n),J(e,0),n.resolve(B),E(e)}function J(e,r,n=new Set,o=e.cacheBudget){const x=[...e.rawChunks.entries()].filter(([B])=>!n.has(B)).sort((B,b)=>B[1].lastUsed-b[1].lastUsed);for(;e.ownedGpuBytes+e.rawReservedBytes+r>o&&x.length;){const[B,b]=x.shift();re(e,B,b)}if(r>0&&e.ownedGpuBytes+e.rawReservedBytes+r>o){const B=new Error(`Chart GPU memory budget (${o} bytes) cannot fit a ${r}-byte allocation`);throw B.webGpuCacheCapacity=!0,B}}function Q(e,r){for(const[n,o]of e.rawRequests)o.id===r&&(o.reject(l.cancellationError(`Raw chunk request superseded for series ${r}`)),Z(e,n,o));for(const[n,o]of e.rawChunks)o.id===r&&re(e,n,o)}function ee(e,r){const n=new Set(r);for(const[o,x]of e.seriesBuffers)n.has(x.id)||(V(e,x),e.seriesBuffers.delete(o),Q(e,x.id),e.uploadGenerations.delete(x.id));for(const[o,x]of e.chunkedUploadSessions)n.has(x.id)||(Y(e,x),e.chunkedUploadSessions.delete(o))}function Y(e,r){for(const n of r.pages??[])n.buffer!==r.overviewBuffer&&l.destroyTrackedBuffer(e,n.buffer);l.destroyTrackedBuffer(e,r.transientBuffer),l.destroyTrackedBuffer(e,r.overviewBuffer),l.destroyTrackedBuffer(e,r.paramsBuffer)}function F(e,r,n){const o=e??r;if(!Number.isSafeInteger(o)||o<0||o>r)throw new Error(`Chunk ${n} length ${o} exceeds payload ${n} length ${r}`);return o}function C(e,r){if(e?._unsafe_create_view){const n=e._unsafe_create_view(),o=F(r,n.byteLength,"byte");if(o%Float32Array.BYTES_PER_ELEMENT!==0)throw new Error(`Chunk byte length ${o} is not aligned to float size`);return n instanceof Float32Array?o===n.byteLength?n:n.subarray(0,o/Float32Array.BYTES_PER_ELEMENT):new Float32Array(n.buffer,n.byteOffset,o/Float32Array.BYTES_PER_ELEMENT)}if(e?.getUint8Array){const n=e.getUint8Array(),o=F(r,n.byteLength,"byte");if(o%Float32Array.BYTES_PER_ELEMENT!==0)throw new Error(`Chunk byte length ${o} is not aligned to float size`);return new Float32Array(n.buffer,n.byteOffset,o/Float32Array.BYTES_PER_ELEMENT)}if(e?.getFloat32Array){const n=e.getFloat32Array(),o=F(r,n.length,"sample");return o===n.length?n:n.subarray(0,o)}if(e instanceof Float32Array){const n=F(r,e.length,"sample");return n===e.length?e:e.subarray(0,n)}if(ArrayBuffer.isView(e)){if(e instanceof Uint8Array||e instanceof Int8Array||e instanceof Uint8ClampedArray){const x=F(r,e.byteLength,"byte");if(x%Float32Array.BYTES_PER_ELEMENT!==0)throw new Error(`Chunk byte length ${x} is not aligned to float size`);return new Float32Array(e.buffer,e.byteOffset,x/Float32Array.BYTES_PER_ELEMENT)}const n=Float32Array.from(e),o=F(r,n.length,"sample");return o===n.length?n:n.subarray(0,o)}if(e instanceof ArrayBuffer){const n=F(r,e.byteLength,"byte");if(n%Float32Array.BYTES_PER_ELEMENT!==0)throw new Error(`Chunk byte length ${n} is not aligned to float size`);return new Float32Array(e,0,n/Float32Array.BYTES_PER_ELEMENT)}if(Array.isArray(e)){const n=Float32Array.from(e),o=F(r,n.length,"sample");return o===n.length?n:n.subarray(0,o)}throw new Error("Synchronous chunk upload requires a MemoryView, typed array, ArrayBuffer, or array payload")}function U(e,r,n,o,x){const B=G.get(e),b=B?.chunkedUploadSessions.get(r);if(!b)throw l.cancellationError(`Chunked series upload ${r} is no longer active`);const t=C(o,x),d=t.length;if(n!==b.writtenLength||n+d>b.length)throw new Error(`Chunked series upload ${r} expected sample offset ${b.writtenLength}, received ${n}`);b.pages?b.pendingValues=t:B.device.queue.writeBuffer(b.transientBuffer,0,t)}function i(e,r){const n=G.get(e),o=n?.chunkedUploadSessions.get(r);o&&(Y(n,o),n.chunkedUploadSessions.delete(r))}function s(e,r){const n=$(r,"Id"),o=$(r,"DataVersion")??0,x=$(r,"Length")??0;return x<2?null:e.seriesBuffers.get(L(n,o,x))??null}function g(e,r,n,o){const x=$(r,"Zoom")??{},b=($(r,"Series")??[]).map(t=>{const d=$(t,"Id"),p=$(t,"DataVersion")??0,P=$(t,"Length")??0,_=$(t,"Color")??{};return[d,p,P,$(t,"SampleStep"),e.seriesBuffers.has(L(d,p,P)),$(t,"OverviewAxisMin"),$(t,"OverviewAxisMax"),$(_,"Red"),$(_,"Green"),$(_,"Blue"),$(_,"Alpha")]});return JSON.stringify([n,o,$(x,"Left")??0,$(x,"Right")??1,$(r,"LineWidth")??.7,$(r,"FillOpacity")??.1,b])}async function h(e,r,n){const o=Math.min(O,Math.max(1,Math.ceil(n/M))),x=o*16;let B=null,b=null,t=null;try{B=l.createTrackedBuffer(e,{size:x,usage:GPUBufferUsage.STORAGE|GPUBufferUsage.COPY_SRC}),b=l.createTrackedBuffer(e,{size:x,usage:GPUBufferUsage.COPY_DST|GPUBufferUsage.MAP_READ}),t=l.createTrackedBuffer(e,{size:16,usage:GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST}),e.device.queue.writeBuffer(t,0,new Uint32Array([n,o,0,0]));const d=e.device.createBindGroup({layout:e.rangePipeline.getBindGroupLayout(0),entries:[{binding:0,resource:{buffer:r}},{binding:1,resource:{buffer:B}},{binding:2,resource:{buffer:t}}]}),p=e.device.createCommandEncoder(),P=p.beginComputePass();P.setPipeline(e.rangePipeline),P.setBindGroup(0,d),P.dispatchWorkgroups(o),P.end(),p.copyBufferToBuffer(B,0,b,0,x),e.device.queue.submit([p.finish()]),await b.mapAsync(GPUMapMode.READ);const _=new DataView(b.getMappedRange());let j=0,W=0,q=!1;for(let D=0;D<o;D++){const oe=D*16;if(_.getUint32(oe+8,!0)===0)continue;const K=_.getFloat32(oe,!0),I=_.getFloat32(oe+4,!0);if(!Number.isFinite(K)||!Number.isFinite(I))throw new Error(`WebGPU range reduction returned non-finite values for workgroup ${D}`);j=q?Math.min(j,K):K,W=q?Math.max(W,I):I,q=!0}return{hasValue:q,minimum:j,maximum:W}}finally{b?.mapState==="mapped"&&b.unmap(),l.destroyTrackedBuffer(e,B),l.destroyTrackedBuffer(e,b),l.destroyTrackedBuffer(e,t)}}function w(e,r){return`${e.id}:${e.version}:${r}`}function a(e,r,n,o){const x=w(r,n),B=e.rawChunks.get(x);if(B)return B.lastUsed=performance.now(),Promise.resolve(B);const b=e.rawRequests.get(x);if(b)return b.promise;const t=n*R;if(t>=r.length)return Promise.resolve(null);const d=Math.min(R+(t+R<r.length?1:0),r.length-t),p=d*Float32Array.BYTES_PER_ELEMENT;J(e,p,o),e.rawReservedBytes+=p;const P=++e.workerRequestId;let _,j;const W=new Promise((I,de)=>{_=I,j=de}),q=l.createTrackedBuffer(e,{size:p,usage:GPUBufferUsage.STORAGE|GPUBufferUsage.COPY_DST}),D={id:r.id,requestId:P,promise:W,resolve:_,reject:j,byteLength:p,buffer:q,offset:t,count:d,writtenLength:0,reservationActive:!0};e.rawRequests.set(x,D);const oe={onerror:I=>{Z(e,x,D),j(new Error(`Raw chunk ${n} generation failed: ${I.message}`))}},K=k.get(r.chartId);return K?K.invokeMethodAsync("ProvideSeriesChunk",r.id,t,d,P).catch(I=>oe.onerror({message:I?.message??I})):oe.onerror({message:`Chart ${r.chartId} is no longer active`}),W}function m(e,r,n,o,x){const B=G.get(e),b=[...B?.rawRequests.values()??[]].find(p=>p.requestId===r);if(!b)throw l.cancellationError(`Raw chunk request ${r} is no longer active`);const t=C(o,x),d=n*Float32Array.BYTES_PER_ELEMENT;if(!b.buffer)throw new Error(`Raw chunk request ${r} has no destination buffer`);if(n!==b.writtenLength||n+t.length>b.count)throw new Error(`Raw chunk request ${r} expected sample offset ${b.writtenLength}, received ${n}`);if(B.device.queue.writeBuffer(b.buffer,d,t),b.writtenLength+=t.length,b.writtenLength===b.count){const p=[...B.rawRequests.entries()].find(([,P])=>P===b)?.[0];if(!p)throw l.cancellationError(`Raw chunk request ${r} was removed before completion`);ne(B,p,b,b.buffer,b.count)}}function E(e){for(const[r,n]of e.lastPayloads)e.previewRenderKeys.delete(r),l.scheduleRender(e.chartId,n).catch(o=>{l.isCancellationError(o)||console.error("[chart-webgpu] raw rerender failed",o)})}function y(e,r,n,o,x,B,b,t){const d=l.getTimeWindow(o,n,r.length);if(!d)return null;const{first:p,last:P,indexLeft:_,indexRange:j}=d,W=P-p;if(!Number.isFinite(W)||W<=0||W>R*2)return null;const q=Math.floor(p/R),D=Math.floor((P-1)/R);for(let K=q;K<=D;K++)t.add(w(r,K));for(let K=q;K<=D;K++)try{a(e,r,K,t).catch(I=>{l.isCancellationError(I)||(l.reportRuntimeFailure(r.chartId,r.lifecycleEpoch,"WebGPU data generation failed",`${I?.message??I} Retry the chart to recreate its GPU resources.`),console.error("[chart-webgpu] raw request failed",I))})}catch(I){if(!I?.webGpuCacheCapacity)throw I}for(const K of[q-1,D+1])if(!(K<0||K>=Math.ceil(r.length/R)))try{a(e,r,K,t).catch(I=>{l.isCancellationError(I)||console.error("[chart-webgpu] raw prefetch failed",I)})}catch(I){l.isCancellationError(I)||console.error("[chart-webgpu] raw prefetch skipped",I)}const oe=[];for(let K=q;K<=D;K++){const I=e.rawChunks.get(w(r,K));if(!I)return null;I.lastUsed=performance.now();const de=Math.max(0,Math.floor(p-I.offset)),we=Math.min(I.length-1,Math.ceil(P-I.offset));if(we<=de)continue;const be={first:de,segmentCount:we-de,zoomedLeft:x.plotLeft+(I.offset+de-_)/j*x.plotWidth,dx:x.plotWidth/j};oe.push(l.getRenderBuffer(e,I,be,x,B,`${b}:${r.id}:${K}`,t))}return oe.length?oe:null}Object.assign(l,{getSeriesKey:L,destroySeriesBuffer:V,destroyRawChunk:re,evictRawChunks:J,removeRawSeries:Q,synchronizeSeries:ee,destroyChunkedUpload:Y,appendChunkedSeries:U,appendSeriesChunk:m,abortChunkedSeries:i,getSeriesBuffer:s,getPreviewRenderKey:g,calculateSeriesRangeAsync:h,rawChunkKey:w,requestRawChunk:a,rerenderLastPayloads:E,getRawRenderItems:y})})(),(function(){const l=window.__nexusChartWebGpu,G=48,k=`
struct Summary {
    start: u32, count: u32, first: f32, last: f32,
    minimum: f32, maximum: f32, minIndex: u32, maxIndex: u32,
    gapIndex: u32, invalidCount: u32, finite: u32, pad: u32,
};
fn invalid(v: f32) -> bool { return (bitcast<u32>(v) & 0x7f800000u) == 0x7f800000u; }
fn sample(index: u32, value: f32) -> Summary {
    let gap = select(0u, 1u, invalid(value));
    return Summary(index, 1u, value, value, value, value, index, index, index, gap, 1u-gap, 0u);
}
fn merge(a: Summary, b: Summary) -> Summary {
    if (a.count == 0u) { return b; }
    if (b.count == 0u) { return a; }
    var s = a;
    s.count += b.count;
    s.last = b.last;
    if (b.finite != 0u) {
        if (a.finite == 0u || b.minimum < a.minimum) { s.minimum = b.minimum; s.minIndex = b.minIndex; }
        if (a.finite == 0u || b.maximum > a.maximum) { s.maximum = b.maximum; s.maxIndex = b.maxIndex; }
    }
    if (a.invalidCount == 0u) { s.gapIndex = b.gapIndex; }
    s.invalidCount += b.invalidCount;
    s.finite |= b.finite;
    return s;
}
`,$=k+`
struct Params { offset: u32, count: u32, phase: u32, firstBucket: u32, pageFirst: u32, pad0: u32, pad1: u32, pad2: u32 };
@group(0) @binding(0) var<storage, read> raw: array<f32>;
@group(0) @binding(1) var<storage, read_write> summaries: array<Summary>;
@group(0) @binding(2) var<uniform> p: Params;
var<workgroup> lanes: array<Summary, 256>;
@compute @workgroup_size(256)
fn base(@builtin(workgroup_id) group: vec3u, @builtin(local_invocation_id) local: vec3u) {
    let bucket = p.firstBucket + group.x;
    let bucketStart = select(bucket * 256u - p.phase, 0u, bucket == 0u);
    let start = max(bucketStart, p.offset);
    let end = min((bucket + 1u) * 256u - p.phase, p.offset + p.count);
    let index = start + local.x;
    var s: Summary;
    if (index < end) { s = sample(index, raw[index - p.offset]); }
    lanes[local.x] = s;
    workgroupBarrier();
    for (var stride = 1u; stride < 256u; stride *= 2u) {
        if (local.x % (stride * 2u) == 0u) { lanes[local.x] = merge(lanes[local.x], lanes[local.x + stride]); }
        workgroupBarrier();
    }
    if (local.x == 0u) {
        if (start > bucketStart) { lanes[0] = merge(summaries[bucket - p.pageFirst], lanes[0]); }
        summaries[bucket - p.pageFirst] = lanes[0];
    }
}
`,R=k+`
struct Params { childOffset: u32, childCount: u32, parentOffset: u32, phase: u32 };
@group(0) @binding(0) var<storage, read> children: array<Summary>;
@group(0) @binding(1) var<storage, read_write> summaries: array<Summary>;
@group(0) @binding(2) var<uniform> p: Params;
@compute @workgroup_size(64)
fn parent(@builtin(global_invocation_id) invocation: vec3u) {
    let i = invocation.x;
    if (i >= (p.childCount + p.phase + 3u) / 4u) { return; }
    var s: Summary;
    for (var j = 0u; j < 4u; j++) {
        let child = i * 4u + j;
        if (child >= p.phase && child - p.phase < p.childCount) {
            s = merge(s, children[p.childOffset + child - p.phase]);
        }
    }
    summaries[p.parentOffset + i] = s;
}
`,M=k+`
struct Part { summary: u32, rawOffset: u32, start: u32, count: u32 };
struct Job { first: u32, count: u32 };
struct Params { jobs: u32, origin: u32, slots: u32, pad: u32 };
@group(0) @binding(0) var<storage, read> summaries: array<Summary>;
@group(0) @binding(1) var<storage, read> raw: array<f32>;
@group(0) @binding(2) var<storage, read> parts: array<Part>;
@group(0) @binding(3) var<storage, read> jobs: array<Job>;
@group(0) @binding(4) var<storage, read_write> points: array<vec2f>;
@group(0) @binding(5) var<uniform> p: Params;
@compute @workgroup_size(64)
fn query(@builtin(global_invocation_id) invocation: vec3u) {
    let id = invocation.x;
    if (id >= p.jobs) { return; }
    let job = jobs[id];
    var s: Summary;
    for (var i = 0u; i < job.count; i++) {
        let part = parts[job.first + i];
        if (part.summary != 0xffffffffu) { s = merge(s, summaries[part.summary]); }
        else {
            for (var j = 0u; j < part.count; j++) { s = merge(s, sample(part.start + j, raw[part.rawOffset + j])); }
        }
    }
    let nan = bitcast<f32>(0x7fc00000u | (id & 1u));
    var indices = array<u32, 5>(s.start, s.start + s.count - 1u, s.minIndex, s.maxIndex, s.gapIndex);
    var values = array<f32, 5>(s.first, s.last, s.minimum, s.maximum, nan);
    var count = select(4u, 5u, s.invalidCount != 0u);
    // Compare against finite count to avoid doubling and overflowing u32.
    if (s.invalidCount >= s.count - s.invalidCount) { indices[0] = s.gapIndex; values[0] = nan; count = 1u; }
    for (var i = 1u; i < count; i++) {
        let index = indices[i]; let value = values[i]; var j = i;
        while (j > 0u) {
            if (indices[j-1u] <= index) { break; }
            indices[j] = indices[j-1u]; values[j] = values[j-1u]; j--;
        }
        indices[j] = index; values[j] = value;
    }
    var written = 0u; var previous = 0xffffffffu; var point = vec2f(0.0, nan);
    for (var i = 0u; i < count; i++) {
        if (indices[i] == previous) { continue; }
        previous = indices[i];
        point = vec2f(f32(indices[i] - p.origin), select(values[i], nan, invalid(values[i])));
        points[id * p.slots + written] = point; written++;
    }
    while (written < p.slots) { points[id * p.slots + written] = point; written++; }
}
`;function O(C,U){const i=[];let s=0;for(let g=256n;;g*=4n){const h=C/g,w=Number((C+BigInt(U)-1n)/g-h+1n);if(i.push({stride:g,first:h,count:w,offset:s}),s+=w,w===1)return{levels:i,bytes:s*G}}}function L(C,U,i,s,g,h){const w=C+BigInt(i),a=C+BigInt(s);let m=1n;if(a-w>BigInt(g)){const n=BigInt(Math.floor(g/5));for(;(n>=2n?(a-w-2n)/m+2n:(a-1n)/m-w/m+1n)>n;)m*=2n}const E=[],y=[],e=[];let r=0;for(let n=w;n<a;){const o=(n/m+1n)*m<a?(n/m+1n)*m:a,x=E.length/4;for(;n<o;){let B;for(const b of h){const t=n/b.stride,d=t*b.stride>C?t*b.stride:C,p=(t+1n)*b.stride<C+BigInt(U)?(t+1n)*b.stride:C+BigInt(U);if(d!==n||p>o)break;B={index:b.offset+Number(t-b.first),end:p}}if(B)E.push(B.index,0,0,0),n=B.end;else{const b=(n/256n+1n)*256n<o?(n/256n+1n)*256n:o,t=Number(b-n),d=Number(n-C);E.push(4294967295,r,d,t);const p=e.at(-1);p&&p.offset+p.count===d?p.count+=t:e.push({offset:d,count:t}),r+=t,n=b}}y.push(x,E.length/4-x)}return{stride:m,parts:new Uint32Array(E),jobs:new Uint32Array(y),ranges:e,rawCount:r,slots:m===1n?1:5}}function V(C){return C.summaryPipelines||(C.summaryPipelines=[$,R,M].map((U,i)=>C.device.createComputePipeline({layout:"auto",compute:{module:C.device.createShaderModule({code:U}),entryPoint:["base","parent","query"][i]}}))),C.summaryPipelines}function re(C,U){const i=Math.floor(Math.min(U,16777216)/G);if(i<4)throw new Error(`GPU storage buffer limit of ${U} bytes cannot fit four summaries.`);const s=[];for(let g=0;g<C/G;g+=i)s.push({first:g,count:Math.min(i,C/G-g)});return{pageRecords:i,pages:s}}function Z(C,U,i,s,g,h=0){for(;s>0;){const w=U.pages[Math.floor(i/U.pageRecords)],a=i-w.first,m=Math.min(s,w.count-a);C.copyBufferToBuffer(w.buffer,a*G,g,h,m*G),i+=m,s-=m,h+=m*G}}function ne(C,U,i,s){const g=C.device.createCommandEncoder(),h=g.beginComputePass();h.setPipeline(U),h.setBindGroup(0,C.device.createBindGroup({layout:U.getBindGroupLayout(0),entries:i.map((w,a)=>({binding:a,resource:{buffer:w}}))})),h.dispatchWorkgroups(s),h.end(),C.device.queue.submit([g.finish()])}async function J(C,U,i,s,g=0n,h){const w=await l.getInstance(C);if(!Number.isInteger(s)||s<2||s>4294966784)throw new Error("Local GPU summaries support 2 through 4,294,966,784 samples; use server reduction for larger domains.");const a=O(BigInt(g),s),m=Math.min(w.device.limits.maxBufferSize,w.device.limits.maxStorageBufferBindingSize),E=re(a.bytes,m),y=[],e=(r,n)=>{const o=l.createTrackedBuffer(w,{size:r,usage:n});return y.push(o),o};try{const r=E.pages.map(b=>({...b,buffer:e(b.count*G,GPUBufferUsage.STORAGE|GPUBufferUsage.COPY_SRC|GPUBufferUsage.COPY_DST)})),n=r[0].buffer,o=e(Math.min(l.streamChunkLength*4,s*4,Math.floor(m/4)*4),GPUBufferUsage.STORAGE|GPUBufferUsage.COPY_DST),x=e(32,GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST),B=++w.uploadToken;return w.chunkedUploadSessions.set(B,{id:U,version:i,length:s,origin:BigInt(g),readRange:h,...a,pageRecords:E.pageRecords,pages:r,overviewBuffer:n,transientBuffer:o,paramsBuffer:x,writtenLength:0,rangeHasValue:!1,rangeMinimum:0,rangeMaximum:0}),B}catch(r){throw y.forEach(n=>l.destroyTrackedBuffer(w,n)),r}}async function Q(C,U,i,s){const g=await l.getInstance(C),h=g.chunkedUploadSessions.get(U);if(!h||h.writtenLength!==i||h.pendingValues?.length!==s)throw l.cancellationError("Invalid or cancelled summary upload.");try{for(let w=0;w<s;){const a=Math.min(s-w,h.transientBuffer.size/4);g.device.queue.writeBuffer(h.transientBuffer,0,h.pendingValues.subarray(w,w+a)),await ee(C,U,i+w,a),w+=a}}finally{h.pendingValues=void 0}}async function ee(C,U,i,s){const g=await l.getInstance(C),h=g.chunkedUploadSessions.get(U);if(!h||h.writtenLength!==i||i+s>h.length)throw l.cancellationError("Invalid or cancelled summary upload.");const w=Number(h.origin%256n),a=Math.floor((i+w)/256),m=Math.floor((i+s-1+w)/256)-a+1;for(let y=a;y<a+m;){const e=h.pages[Math.floor(y/h.pageRecords)],r=Math.min(a+m-y,e.first+e.count-y,65535);g.device.queue.writeBuffer(h.paramsBuffer,0,new Uint32Array([i,s,w,y,e.first,0,0,0])),ne(g,V(g)[0],[h.transientBuffer,e.buffer,h.paramsBuffer],r),y+=r}const E=await l.calculateSeriesRangeAsync(g,h.transientBuffer,s);if(g.chunkedUploadSessions.get(U)!==h)throw l.cancellationError("Summary upload cancelled.");E.hasValue&&(h.rangeMinimum=h.rangeHasValue?Math.min(h.rangeMinimum,E.minimum):E.minimum,h.rangeMaximum=h.rangeHasValue?Math.max(h.rangeMaximum,E.maximum):E.maximum,h.rangeHasValue=!0),h.writtenLength+=s}async function Y(C,U){const i=await l.getInstance(C),s=i.chunkedUploadSessions.get(U);if(!s||s.writtenLength!==s.length)throw l.cancellationError("Incomplete summary upload.");const g=l.createTrackedBuffer(i,{size:Math.min(s.levels[0].count,s.pageRecords)*G,usage:GPUBufferUsage.STORAGE|GPUBufferUsage.COPY_DST});try{for(let h=1;h<s.levels.length;h++){const w=s.levels[h-1],a=s.levels[h],m=Number(w.first%4n);for(let E=0;E<a.count;){const y=s.pages[Math.floor((a.offset+E)/s.pageRecords)],e=Math.min(a.count-E,y.first+y.count-a.offset-E,Math.floor(s.pageRecords/4),65535*64),r=Math.max(0,E*4-m),n=Math.min(w.count,(E+e)*4-m),o=i.device.createCommandEncoder();Z(o,s,w.offset+r,n-r,g),i.device.queue.submit([o.finish()]),i.device.queue.writeBuffer(s.paramsBuffer,0,new Uint32Array([0,n-r,a.offset+E-y.first,E===0?m:0])),ne(i,V(i)[1],[g,y.buffer,s.paramsBuffer],Math.ceil(e/64)),E+=e}}}finally{l.destroyTrackedBuffer(i,g)}for(const[h,w]of i.seriesBuffers)w.id===s.id&&(l.destroySeriesBuffer(i,w),i.seriesBuffers.delete(h));return i.seriesBuffers.set(l.getSeriesKey(s.id,s.version,s.length),{...s,buffer:s.overviewBuffer,pointBuffer:s.overviewBuffer,pyramid:!0,decimations:new Map}),l.destroyTrackedBuffer(i,s.transientBuffer),l.destroyTrackedBuffer(i,s.paramsBuffer),i.chunkedUploadSessions.delete(U),{hasValue:s.rangeHasValue,minimum:s.rangeMinimum,maximum:s.rangeMaximum}}async function F(C,U,i,s,g,h,w=()=>!0){const a=l.getTimeWindow(s,i,U.length);if(!a)return null;const m=l.valueOf(i,"ViewFirst")??Math.max(0,a.first-1),E=l.valueOf(i,"ViewEnd")??Math.min(U.length,Math.ceil(a.indexLeft+a.indexRange)+1),y=l.valueOf(i,"PointBudget")??5*(Math.min(8192,Math.max(2,Math.ceil(g.plotWidth*4)))+1),e=`${m}:${E}:${y}`;let r=U.decimations.get(h);if(r?.key!==e){const n=L(U.origin,U.length,m,E,y,U.levels),o=new Float32Array(n.rawCount);let x=0;for(const t of n.ranges){const d=await U.readRange(t.offset,t.count);if(!w())return null;if(d.length!==t.count)throw new Error("Incomplete raw summary boundary.");o.set(d,x),x+=d.length}if(C.disposed||![...C.seriesBuffers.values()].includes(U))throw l.cancellationError("Summary view superseded.");const B=[],b=(t,d)=>{const p=Math.max(8,typeof t=="number"?t:t.byteLength),P=Math.min(C.device.limits.maxBufferSize,C.device.limits.maxStorageBufferBindingSize);if(p>P)throw new Error(`Bounded viewport query requires ${p} bytes, exceeding the GPU binding limit of ${P} bytes.`);const _=l.createTrackedBuffer(C,{size:p,usage:d});return B.push(_),typeof t!="number"&&t.byteLength&&C.device.queue.writeBuffer(_,0,t),_};try{const t=n.jobs.length/2,d=b(t*n.slots*8,GPUBufferUsage.STORAGE|GPUBufferUsage.COPY_SRC),p=GPUBufferUsage.STORAGE|GPUBufferUsage.COPY_DST;let P=0;for(let I=0;I<n.parts.length;I+=4)n.parts[I]!==4294967295&&P++;const _=b(Math.max(G,P*G),p),j=C.device.createCommandEncoder();let W=0;for(let I=0;I<n.parts.length;I+=4)n.parts[I]!==4294967295&&(Z(j,U,n.parts[I],1,_,W*G),n.parts[I]=W++);C.device.queue.submit([j.finish()]);const q=b(o,p),D=b(n.parts,p),oe=b(n.jobs,p),K=b(new Uint32Array([t,m,n.slots,0]),GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST);ne(C,V(C)[2],[_,q,D,oe,d,K],Math.ceil(t/64)),l.destroyTrackedBuffer(C,r?.outputBuffer),r={key:e,outputBuffer:d,length:t*n.slots,renderBuffer:{buffer:d,pointBuffer:d,dataMode:1}},U.decimations.set(h,r);for(const I of B.slice(1))l.destroyTrackedBuffer(C,I)}catch(t){throw B.forEach(d=>l.destroyTrackedBuffer(C,d)),t}}return{seriesBuffer:r.renderBuffer,zoomInfo:{first:0,segmentCount:Math.max(0,r.length-1),zoomedLeft:g.plotLeft+(m-a.indexLeft)/a.indexRange*g.plotWidth,dx:g.plotWidth/a.indexRange}}}Object.assign(l,{summaryWgsl:k,baseSummaryShader:$,parentSummaryShader:R,querySummaryShader:M,pyramidLayout:O,summaryPages:re,planQuery:L,getPyramidRenderItem:F,beginChunkedSeriesAsync:J,processChunkedSeriesUploadAsync:Q,completeChunkedSeriesAsync:Y})})(),(function(){const l=window.__nexusChartWebGpu,{instances:G,pendingInstances:k,lifecycleEpochs:$,failureStates:R,dotNetHelpers:M,configuredCacheBudgets:O,valueOf:L,colorOf:V,ensureCanvasSize:re,getCanvasContext:Z,releaseCanvasContext:ne,getReducedOutputLength:J,getSharedGpu:Q,getInstance:ee,getLifecycleEpoch:Y,advanceLifecycleEpoch:F,isCancellationError:C,reportRuntimeFailure:U,destroyInstance:i,releaseSharedGpuIfUnused:s,evictRawChunks:g,createTrackedBuffer:h,destroyTrackedBuffer:w,synchronizeSeries:a,beginChunkedSeriesAsync:m,appendChunkedSeries:E,processChunkedSeriesUploadAsync:y,completeChunkedSeriesAsync:e,abortChunkedSeries:r,appendSeriesChunk:n,getSeriesBuffer:o,getPreviewRenderKey:x,getRawRenderItems:B,uniformBufferSize:b,fillVerticesPerSegment:t,lineVerticesPerSegment:d,decimationFactor:p,decimationBucketsPerPixel:P,maxDecimationBuckets:_,overviewBucketSize:j,reducedPointsPerBucket:W,rawChunkLength:q}=l,D=new Map;function oe(u,c){return`${u}:${c}`}function K(u){for(const[c,v]of D)c.startsWith(`${u}:`)&&(v.generation++,v.pending=null,D.delete(c))}function I(u,c){for(const v of[...u.seriesBuffers.values(),...u.rawChunks.values()])for(const[S,f]of v.decimations??[])S!==c&&!S.startsWith(`${c}:`)||(w(u,f.outputBuffer),w(u,f.paramsBuffer),v.decimations.delete(S))}function de(u,c){const v=oe(u,c),S=D.get(v);S&&(S.generation++,S.pending=null,D.delete(v));const f=G.get(u);if(f){f.lastPayloads.delete(c),f.previewRenderKeys.delete(c),be(f,c,0),ne(f,c),I(f,c);for(const[z,A]of f.seriesBuffers)A.remoteTarget===c&&(l.destroySeriesBuffer(f,A),f.seriesBuffers.delete(z))}}function we(u,c,v,S,f){let z=u.targetResources.get(S);z||(z=[],u.targetResources.set(S,z));let A=z[v];if(A?.seriesBuffer===c)return A;w(u,A?.uniformBuffer);const T=h(u,{size:b,usage:GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST},f),X=u.device.createBindGroup({layout:u.pipeline.getBindGroupLayout(0),entries:[{binding:0,resource:{buffer:T}},{binding:1,resource:{buffer:c.buffer}},{binding:2,resource:{buffer:c.pointBuffer}}]});return A={seriesBuffer:c,uniformBuffer:T,bindGroup:X},z[v]=A,A}function be(u,c,v){const S=u.targetResources.get(c);if(!(!S||S.length<=v)){for(const f of S.splice(v))w(u,f.uniformBuffer);S.length===0&&u.targetResources.delete(c)}}function Ue(u,c,v){const S=L(u,"Plot")??{},f=(L(S,"Left")??0)*c,z=(L(S,"Top")??0)*v,A=(L(S,"Right")??1)*c,T=(L(S,"Bottom")??1)*v,X=A-f,N=T-z;return X<=0||N<=0?null:{plotLeft:f,plotTop:z,plotRight:A,plotBottom:T,plotWidth:X,plotHeight:N}}function ke(u,c,v){const S=L(u,"Zoom")??{},f=L(S,"Left")??0,z=L(S,"Right")??1,A=L(c,"SampleStep"),T=f/A,X=z/A,N=X-T;if(!Number.isFinite(A)||A<=0||!Number.isFinite(N)||N<=0)return null;const ue=Math.max(0,Math.floor(T)),ae=Math.min(v-1,Math.ceil(X));return ae<ue?null:{first:ue,last:ae,indexLeft:T,indexRange:N}}function Se(u,c,v,S){const f=ke(u,c,v);if(!f)return null;const{first:z,last:A,indexLeft:T,indexRange:X}=f,N=A-z+1,ue=S.plotLeft+(z-T)/X*S.plotWidth,ae=S.plotWidth/X;return N<2||!Number.isFinite(ae)||ae<=0?null:{first:z,segmentCount:N-1,zoomedLeft:ue,dx:ae}}function Pe(u,c,v,S){const f=ke(u,c,v.length);if(!f)return null;const{first:z,last:A,indexLeft:T,indexRange:X}=f,N=Math.max(0,Math.floor(z/j)-1),ue=Math.min(v.overviewBucketCount-1,Math.ceil(A/j)),ae=N*W,le=(ue-N+1)*W;return le<2?null:{first:ae,segmentCount:le-1,zoomedLeft:S.plotLeft,dx:S.plotWidth/(X/j),xOrigin:T/j}}function Me(u,c,v,S,f,z,A){const T=v.segmentCount+1,X=Math.min(_,Math.max(2,Math.ceil(S.plotWidth*P)));if(T<=X*p)return{seriesBuffer:c,zoomInfo:v};let N=c.decimations.get(z);const ue=J(X),ae=ue*2*Float32Array.BYTES_PER_ELEMENT;if(!N||N.bucketCount!==X){N&&(w(u,N.outputBuffer),w(u,N.paramsBuffer));let ge=null,fe=null;try{ge=h(u,{size:ae,usage:GPUBufferUsage.STORAGE},A),fe=h(u,{size:16,usage:GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST},A)}catch(he){throw w(u,ge),w(u,fe),he}const se=u.device.createBindGroup({layout:(c.dataMode===1?u.pointDecimationPipeline:u.decimationPipeline).getBindGroupLayout(0),entries:[{binding:0,resource:{buffer:c.dataMode===1?c.pointBuffer:c.buffer}},{binding:1,resource:{buffer:ge}},{binding:2,resource:{buffer:fe}}]});N={bucketCount:X,outputBuffer:ge,paramsBuffer:fe,bindGroup:se,renderBuffer:{buffer:c.buffer,pointBuffer:ge,length:ue,dataMode:1}},c.decimations.set(z,N)}u.device.queue.writeBuffer(N.paramsBuffer,0,new Uint32Array([v.first,T,X,c.overviewLength??c.length]));const le=f.beginComputePass();return le.setPipeline(c.dataMode===1?u.pointDecimationPipeline:u.decimationPipeline),le.setBindGroup(0,N.bindGroup),le.dispatchWorkgroups(X),le.end(),{seriesBuffer:N.renderBuffer,zoomInfo:{first:0,segmentCount:ue-1,zoomedLeft:v.zoomedLeft,dx:v.dx,xOrigin:v.xOrigin??0}}}function Re(u,c,v,S,f,z,A,T,X,N,ue){const ae=L(S,"Preview")??!1,le=ae?L(f,"OverviewAxisMin")??0:L(f,"AxisMin")??0,fe=(ae?L(f,"OverviewAxisMax")??1:L(f,"AxisMax")??1)-le;if(!Number.isFinite(fe)||fe===0)return!1;const se=(L(S,"LineWidth")??.7)*N,he=L(S,"FillOpacity")??.1,ve=Math.min(z.plotBottom,Math.max(z.plotTop,z.plotBottom-(0-le)/fe*z.plotHeight)),pe=V(f),ye=new ArrayBuffer(b),H=new Float32Array(ye),ce=new Uint32Array(ye);return H[0]=T,H[1]=X,H[4]=z.plotLeft,H[5]=z.plotTop,H[6]=z.plotRight,H[7]=z.plotBottom,H[8]=le,H[9]=fe,H[10]=A.zoomedLeft,H[11]=A.dx,H[12]=ve,H[13]=se,H[14]=he,H[16]=pe[0],H[17]=pe[1],H[18]=pe[2],H[19]=pe[3],ce[20]=A.first,ce[21]=ue,ce[22]=v.dataMode??0,H[23]=A.xOrigin??0,u.device.queue.writeBuffer(c,0,ye),!0}async function xe(u,c,v){const S=Y(u);try{return await v()}catch(f){throw C(f)||U(u,S,c,`${f?.message??f} Retry the chart to recreate its GPU resources.`),f}}function Ee(u,c,v,S){const f=L(c,"RemotePoints"),z=L(c,"Id"),A=JSON.stringify(["remote",v,z]);let T=u.seriesBuffers.get(A);if(!(f instanceof Float32Array)||f.length%2||f.length>32768*2)throw new Error("Invalid remote visualization points.");if(T?.remotePoints!==f){for(let N=0;N<f.length;N+=2)if(!Number.isFinite(f[N])||N>0&&f[N]<f[N-2])throw new Error("Invalid remote visualization coordinates.");const X=h(u,{size:Math.max(8,f.byteLength),usage:GPUBufferUsage.STORAGE|GPUBufferUsage.COPY_DST});f.length&&u.device.queue.writeBuffer(X,0,f),T&&l.destroySeriesBuffer(u,T),T={id:z,buffer:X,pointBuffer:X,dataMode:1,remoteTarget:v,remotePoints:f},u.seriesBuffers.set(A,T)}return{seriesBuffer:T,zoomInfo:{first:0,segmentCount:Math.max(0,f.length/2-1),zoomedLeft:S.plotLeft,dx:S.plotWidth,xOrigin:0}}}async function Le(u,c,v=null,S=0){const f=await ee(u);if(!f)return;const{device:z,pipeline:A}=f,T=L(c,"Target")??"series";if(v&&(v.generation!==S||D.get(oe(u,T))!==v))throw l.cancellationError(`Rendering target ${T} was superseded`);const X=document.getElementById(`${T}_${u}`);if(!X){ne(f,T);return}const N=Z(f,T,X),{width:ue,height:ae,dpr:le}=re(X),ge=L(c,"Preview")??!1;f.lastPayloads.set(T,c);let fe=null;if(ge&&!(L(c,"Series")??[]).some(ce=>L(ce,"RemotePoints"))&&(fe=x(f,c,ue,ae),f.previewRenderKeys.get(T)===fe))return;const se=Ue(c,ue,ae),he=z.createCommandEncoder(),ve=[],pe=new Set;let ye=0;if(se){const ce=L(c,"Series")??[];for(const[ie,te]of f.seriesBuffers)te.remoteTarget===T&&!ce.some(me=>L(me,"Id")===te.id)&&(l.destroySeriesBuffer(f,te),f.seriesBuffers.delete(ie));for(const ie of ce){if(L(ie,"RemotePoints")){ve.push({series:ie,...Ee(f,ie,T,se)});continue}const te=o(f,ie);if(!te)continue;if(te.chartId=u,te.generation=f.uploadGenerations.get(te.id),te.lifecycleEpoch=Y(u),te.pyramid){const Be=await l.getPyramidRenderItem(f,te,ie,c,se,T,()=>!v||!v.pending&&v.generation===S&&D.get(oe(u,T))===v);if(v&&(v.pending||v.generation!==S||D.get(oe(u,T))!==v))return;Be&&ve.push({series:ie,...Be});continue}if(te.chunked){const Be=B(f,te,ie,c,se,he,T,pe);if(Be){for(const Ge of Be)ve.push({series:ie,...Ge});continue}}const me=te.chunked?Pe(c,ie,te,se):Se(c,ie,te.length,se);if(!me)continue;const $e=Me(f,te,me,se,he,T,pe);ve.push({series:ie,...$e})}}const H=he.beginRenderPass({colorAttachments:[{view:N.getCurrentTexture().createView(),loadOp:"clear",storeOp:"store",clearValue:{r:0,g:0,b:0,a:0}}]});if(se){H.setPipeline(A),H.setScissorRect(Math.max(0,Math.floor(se.plotLeft)),Math.max(0,Math.floor(se.plotTop)),Math.max(1,Math.ceil(se.plotWidth)),Math.max(1,Math.ceil(se.plotHeight)));for(const{series:ce,seriesBuffer:ie,zoomInfo:te}of ve){const me=we(f,ie,ye++,T,pe);Re(f,me.uniformBuffer,ie,c,ce,se,te,ue,ae,le,0)&&(H.setBindGroup(0,me.bindGroup),H.draw(te.segmentCount*t))}for(const{series:ce,seriesBuffer:ie,zoomInfo:te}of ve){const me=we(f,ie,ye++,T,pe);Re(f,me.uniformBuffer,ie,c,ce,se,te,ue,ae,le,1)&&(H.setBindGroup(0,me.bindGroup),H.draw(te.segmentCount*d))}}H.end(),z.queue.submit([he.finish()]),be(f,T,ye),fe!==null&&f.previewRenderKeys.set(T,fe)}function Ce(u,c){const v=L(c,"Target")??"series",S=oe(u,v);let f=D.get(S);if(f||(f={generation:0,pending:null,running:!1},D.set(S,f)),f.pending=c,f.running)return f.promise;f.running=!0;const z=f.generation;return f.promise=(async()=>{try{for(;f.pending&&f.generation===z;){const A=f.pending;f.pending=null,await xe(u,"WebGPU rendering failed",()=>Le(u,A,f,z))}}finally{f.running=!1,D.get(S)===f&&!f.pending&&D.delete(S)}})(),f.promise}Object.assign(l,{getRenderBuffer:Me,getTimeWindow:ke,runRuntimeOperation:xe,renderSeriesAsync:Le,scheduleRender:Ce,releaseTarget:de,invalidateChartRenders:K}),window.nexus??={},window.nexus.chartWebGpu={initialize(u,c){M.set(u,c);const v=R.get(u);if(v){c.invokeMethodAsync("WebGpuFailed",v.title,v.message).catch(S=>console.error("[chart-webgpu] failure callback failed",S));return}ee(u).catch(S=>console.error("[chart-webgpu] initialization failed",S))},setCacheBudget(u,c){if(!Number.isSafeInteger(c)||c<0)throw new Error(`Cache budget must be a non-negative safe integer (received ${c})`);O.set(u,c);const v=G.get(u);return v&&(v.cacheBudget=c,g(v,0,new Set,c)),!v||v.ownedGpuBytes+v.rawReservedBytes<=c},synchronizeSeries(u,c){const v=G.get(u);v&&a(v,c)},beginChunkedSeries(u,c,v,S,f,z){return xe(u,"WebGPU upload failed",()=>m(u,c,v,S,f,z))},appendChunkedSeries(u,c,v,S,f){const z=Y(u);try{E(u,c,v,S,f)}catch(A){throw C(A)||U(u,z,"WebGPU upload failed",`${A?.message??A} Retry the chart to recreate its GPU resources.`),A}},processChunkedSeriesUpload(u,c,v,S){return xe(u,"WebGPU upload failed",()=>y(u,c,v,S))},completeChunkedSeries(u,c){return xe(u,"WebGPU upload failed",()=>e(u,c))},abortChunkedSeries(u,c){r(u,c)},appendSeriesChunk(u,c,v,S,f){n(u,c,v,S,f)},renderSeries(u,c){Ce(u,c).catch(v=>{C(v)||console.error("[chart-webgpu] render failed",v)})},releaseTarget:de,async retry(u){F(u),k.delete(u);const c=G.get(u);return c&&(G.delete(u),i(c,`Chart ${u} WebGPU retry`)),R.delete(u),await ee(u)!==null},dispose(u){const c=k.get(u);F(u),k.delete(u);const v=G.get(u);G.delete(u),v&&i(v,`Chart ${u} was disposed`),O.delete(u),R.delete(u),M.delete(u),c||$.delete(u),s()}},globalThis.__nexusChartWebGpuTestHooks&&Object.assign(globalThis.__nexusChartWebGpuTestHooks,{instances:G,pendingInstances:k,lifecycleEpochs:$,failureStates:R,getSharedGpu:Q,getInstance:ee,runRuntimeOperation:xe,evictRawChunks:g,trimDrawResources:be,colorOf:V,getCanvasContext:Z,releaseCanvasContext:ne,getReducedOutputLength:J,reducedPointsPerBucket:W,getTimeWindow:ke,getZoomInfo:Se,getOverviewZoom:Pe,getRemoteRenderItem:Ee,getRawRenderItems:B,scheduleRender:Ce,releaseTarget:de,renderStates:D,createTrackedBuffer:h,destroyTrackedBuffer:w,releaseSharedGpuIfUnused:s,rawChunkLength:q})})();
