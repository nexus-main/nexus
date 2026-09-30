window.nexus={},nexus.chart={},nexus.chart.charts={},nexus.chart.resize=function(s,R,g,W,m,x){let _=document.getElementById(`${R}_${s}`);_.style.left=`${g*100}%`,_.style.top=`${W*100}%`,_.style.width=`${(m-g)*100}%`,_.style.height=`${(x-W)*100}%`},nexus.chart.setTextContent=function(s,R,g){let W=document.getElementById(`${R}_${s}`);W.textContent=g},nexus.chart.translate=function(s,R,g,W){let m=document.getElementById(`${R}_${s}`);m.style.removeProperty("display"),m.style.left=`${g*100}%`,m.style.top=`${W*100}%`},nexus.chart.hide=function(s,R){let g=document.getElementById(`${R}_${s}`);g.style.display="none"},nexus.chart.updateAuxiliary=function(s,R,g,W,m){nexus.chart.setTextContent(s,"value_datetime",W),nexus.chart.translate(s,"crosshairs-x",0,g),nexus.chart.translate(s,"crosshairs-y",R,0);for(const x of m){const _=x.id??x.Id,F=x.visible??x.Visible,$=x.text??x.Text;F?nexus.chart.translate(s,`pointer_${_}`,x.x??x.X,x.y??x.Y):nexus.chart.hide(s,`pointer_${_}`),nexus.chart.setTextContent(s,`value_${_}`,$)}},nexus.chart.clearAuxiliary=function(s){for(const g of["crosshairs-x","crosshairs-y"]){const W=document.getElementById(`${g}_${s}`);W&&(W.style.display="none")}const R=document.getElementById(`chart_${s}`);for(const g of R?.querySelectorAll(".pointer")??[])g.style.display="none";for(const g of R?.parentElement?.querySelectorAll('[id^="value_"]')??[])g.textContent="--"},nexus.chart.toRelative=function(s,R,g){let m=document.getElementById(`overlay_${s}`).getBoundingClientRect(),x=(R-m.left)/m.width,_=(g-m.top)/m.height;return x=Math.max(0,x),x=Math.min(1,x),_=Math.max(0,_),_=Math.min(1,_),{x,y:_}},nexus.chart.initInteractions=function(s,R){const g=document.getElementById(`overlay_${s}`),W=document.getElementById(`selection_${s}`),m=[],x=(l,b,P)=>Math.max(b,Math.min(l,P)),_=(l,b)=>{const P=typeof l=="number"?l:Number.parseFloat(l);return Number.isFinite(P)?P:b},F=(l,b)=>{const P=b-l;return P>=1?[0,1]:l<0?[0,P]:b>1?[1-P,1]:[l,b]},$=(l,b,P,G,N)=>{l=x(_(l,0),0,1),b=x(_(b,1),l,1),P=x(_(P,.5),0,1),G=_(G,1),N=x(_(N,Number.EPSILON),Number.MIN_VALUE,1);let c=b-l;if(c<N){const B=x((l+b)/2,0,1);l=x(B-N/2,0,1-N),b=l+N,c=N}const y=l+P*c,T=Number.EPSILON*Math.max(1,Math.abs(l),Math.abs(b)),w=c*G,k=x(G>1?Math.max(w,c+T):w,N,1);return F(y-P*k,y+(1-P)*k)};let se=!1,ne=null,I=!1,K=null,ce=null,Y=!1;function Z(l,b){ne={method:l,values:b},!se&&(se=!0,requestAnimationFrame(async()=>{const P=ne;ne=null;try{!Y&&P&&(await R.invokeMethodAsync(P.method,...P.values),!Y&&ce&&ee(...ce))}catch(G){console.error("[chart] zoom update failed",G)}finally{se=!1,!Y&&ne&&Z(ne.method,ne.values)}}))}function A(l,b,P,G){l.addEventListener(b,P,G),m.push(()=>l.removeEventListener(b,P,G))}function ee(l,b){K=[l,b],!I&&(I=!0,requestAnimationFrame(async()=>{const P=K;K=null;try{!Y&&P&&await R.invokeMethodAsync("PointerMoved",...P)}catch(G){console.error("[chart] pointer update failed",G)}finally{I=!1,!Y&&K&&ee(...K)}}))}if(g&&W){let l=null,b=null;const P=20,G=(c,y,T,w,k)=>{const B=Math.abs(T-c)*k.width,L=Math.abs(w-y)*k.height,C=B>L&&L<=P,U=L>B&&B<=P;return{left:C||!U?Math.min(c,T):0,right:C||!U?Math.max(c,T):1,top:U||!C?Math.min(y,w):0,bottom:U||!C?Math.max(y,w):1}};A(g,"mousemove",c=>{const y=g.getBoundingClientRect();y.width<=0||y.height<=0||(ce=[x((c.clientX-y.left)/y.width,0,1),x((c.clientY-y.top)/y.height,0,1)],ee(...ce))}),A(g,"mouseleave",()=>{ce=null,K=null,Y||nexus.chart.clearAuxiliary(s)}),A(g,"wheel",c=>{c.cancelable&&c.preventDefault();const y=g.getBoundingClientRect(),T=x((c.clientX-y.left)/y.width,0,1),w=x((c.clientY-y.top)/y.height,0,1);if(c.shiftKey)Z("WheelZoom",[T,w,c.deltaY,!0]);else{const k=$(g.dataset.zoomLeft,g.dataset.zoomRight,T,c.deltaY<0?.85:1.1764705882352942,g.dataset.minimumHorizontalZoom);let B=x(_(g.dataset.zoomTop,0),0,1),L=x(_(g.dataset.zoomBottom,1),B,1);c.deltaY>0&&k[0]===0&&k[1]===1&&([B,L]=$(B,L,w,1/.85,1e-6),B=Math.fround(B),L=Math.fround(L)),g.dataset.zoomLeft=k[0].toString(),g.dataset.zoomRight=k[1].toString(),g.dataset.zoomTop=B.toString(),g.dataset.zoomBottom=L.toString(),Z("SetViewport",[k[0],B,k[1],L])}},{passive:!1}),A(g,"pointerdown",c=>{if(c.button!==0&&c.button!==1)return;c.cancelable&&c.preventDefault();const y=g.getBoundingClientRect(),T=x((c.clientX-y.left)/y.width,0,1),w=x((c.clientY-y.top)/y.height,0,1);if(c.pointerType==="touch"||c.pointerType==="pen"){const k=_(c.timeStamp,Date.now()),B=b;if(b={time:k,x:c.clientX,y:c.clientY},B&&k-B.time<=350&&Math.hypot(c.clientX-B.x,c.clientY-B.y)<=24){b=null,g.dataset.zoomLeft="0",g.dataset.zoomRight="1",g.dataset.zoomTop="0",g.dataset.zoomBottom="1",W.style.display="none",l=null,Z("SetViewport",[0,0,1,1]);return}}g.setPointerCapture(c.pointerId),l={pointerId:c.pointerId,rect:y,startX:T,startY:w,currentX:0,currentY:0,pan:c.button===1||c.altKey||c.ctrlKey||c.metaKey,zoom:{left:parseFloat(g.dataset.zoomLeft),top:parseFloat(g.dataset.zoomTop),right:parseFloat(g.dataset.zoomRight),bottom:parseFloat(g.dataset.zoomBottom)}},l.currentX=l.startX,l.currentY=l.startY}),A(g,"pointermove",c=>{if(!l||l.pointerId!==c.pointerId)return;const y=x((c.clientX-l.rect.left)/l.rect.width,0,1),T=x((c.clientY-l.rect.top)/l.rect.height,0,1);if(l.currentX=y,l.currentY=T,l.pan){const C=l.zoom.right-l.zoom.left,U=l.zoom.bottom-l.zoom.top,j=x(l.zoom.left-(y-l.startX)*C,0,1-C),me=x(l.zoom.top-(T-l.startY)*U,0,1-U);Z("SetViewport",[j,me,j+C,me+U]);return}const{left:w,top:k,right:B,bottom:L}=G(l.startX,l.startY,y,T,l.rect);Object.assign(W.style,{display:"block",left:`${w*100}%`,top:`${k*100}%`,width:`${(B-w)*100}%`,height:`${(L-k)*100}%`})});const N=c=>{if(!l||l.pointerId!==c.pointerId)return;W.style.display="none";const y=l;if(l=null,y.pan)return;const T=Math.abs(y.currentX-y.startX)*y.rect.width,w=Math.abs(y.currentY-y.startY)*y.rect.height;if(Math.hypot(T,w)<6)return;const{left:k,top:B,right:L,bottom:C}=G(y.startX,y.startY,y.currentX,y.currentY,y.rect);Z("DragZoom",[k,B,L,C])};A(g,"pointerup",N),A(g,"pointercancel",N)}function xe(l){const b=document.getElementById(`${l}-track_${s}`),P=document.getElementById(`${l}-window_${s}`),G=document.getElementById(`${l}-handle-left_${s}`),N=document.getElementById(`${l}-handle-right_${s}`);if(!b||!P||!G||!N)return;let c=null;function y(w,k){k.button===0&&(k.cancelable&&k.preventDefault(),k.stopPropagation(),b.setPointerCapture(k.pointerId),c={mode:w,pointerId:k.pointerId,rect:b.getBoundingClientRect(),startX:k.clientX,startLeft:parseFloat(P.dataset.left),startRight:parseFloat(P.dataset.right),domainLeft:parseFloat(b.dataset.domainLeft),domainRight:parseFloat(b.dataset.domainRight)})}A(G,"pointerdown",w=>y("left",w)),A(N,"pointerdown",w=>y("right",w)),A(P,"pointerdown",w=>y("pan",w)),A(b,"pointerdown",w=>{if(w.target!==b&&w.target.tagName!=="CANVAS")return;const k=b.getBoundingClientRect(),B=parseFloat(b.dataset.domainLeft),L=parseFloat(b.dataset.domainRight),C=B+x((w.clientX-k.left)/k.width,0,1)*(L-B),U=parseFloat(P.dataset.right)-parseFloat(P.dataset.left),j=x(C-U/2,0,1-U);Z("NavigatorZoom",[j,j+U])}),A(b,"pointermove",w=>{if(!c||c.pointerId!==w.pointerId)return;w.cancelable&&w.preventDefault();const k=c.domainRight-c.domainLeft,B=(w.clientX-c.startX)/c.rect.width*k,L=Math.max(Number.EPSILON,k/Math.max(1,c.rect.width*4));let C,U;if(c.mode==="left")C=x(c.startLeft+B,0,c.startRight-L),U=c.startRight;else if(c.mode==="right")C=c.startLeft,U=x(c.startRight+B,c.startLeft+L,1);else{const j=c.startRight-c.startLeft;C=x(c.startLeft+B,0,1-j),U=C+j}Z("NavigatorZoom",[C,U])});const T=w=>{c?.pointerId===w.pointerId&&(c=null)};A(b,"pointerup",T),A(b,"pointercancel",T),A(b,"wheel",w=>{w.cancelable&&w.preventDefault();const k=b.getBoundingClientRect(),B=parseFloat(b.dataset.domainLeft),L=parseFloat(b.dataset.domainRight),C=B+x((w.clientX-k.left)/k.width,0,1)*(L-B),U=parseFloat(P.dataset.left),j=parseFloat(P.dataset.right),me=w.deltaY<0?.9:1.1111111111111112;Z("NavigatorZoom",F(C-(C-U)*me,C+(j-C)*me))},{passive:!1}),A(P,"keydown",w=>{if(w.key!=="ArrowLeft"&&w.key!=="ArrowRight")return;w.cancelable&&w.preventDefault();const k=w.key==="ArrowLeft"?-1:1,B=parseFloat(P.dataset.left),L=parseFloat(P.dataset.right),C=L-B,U=C*(w.shiftKey?.1:.02)*k;if(w.altKey){const j=x(L+U,B+Number.EPSILON,1);Z("NavigatorZoom",[B,j])}else{const j=x(B+U,0,1-C);Z("NavigatorZoom",[j,j+C])}})}xe("navigator"),xe("navigator-detail"),nexus.chart.charts[s]={dispose:()=>{Y=!0,K=null,ne=null,m.forEach(l=>l())}}},nexus.chart.dispose=function(s){nexus.chart.charts[s]?.dispose(),delete nexus.chart.charts[s]},(function(){const s=window.__nexusChartWebGpu={},R=96,g=6,W=18,m=64,x=4,_=2,F=8192,$=256,se=1024,ne=512*1024*1024,I=256,K=3,ce=4*1024*1024,Y=1024*1024,Z=`
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
`,A=`
struct Params {
    globalOffset: u32,
    sourceLength: u32,
    outputBucket: u32,
    _pad: u32,
};

@group(0) @binding(0) var<storage, read> source: array<f32>;
@group(0) @binding(1) var<storage, read_write> output: array<vec2f>;
@group(0) @binding(2) var<uniform> params: Params;

var<workgroup> minimums: array<f32, ${I}>;
var<workgroup> maximums: array<f32, ${I}>;
var<workgroup> minimumIndices: array<u32, ${I}>;
var<workgroup> maximumIndices: array<u32, ${I}>;
var<workgroup> valid: array<u32, ${I}>;
var<workgroup> nanSeen: array<u32, ${I}>;
var<workgroup> nanIndices: array<u32, ${I}>;
var<workgroup> nanRunCounts: array<u32, ${I}>;

fn isNonFinite(x: f32) -> bool {
    let bits = bitcast<u32>(x);
    return (bits & 0x7f800000u) == 0x7f800000u;
}

@compute @workgroup_size(${I})
fn reduceOverview(@builtin(workgroup_id) groupId: vec3u, @builtin(local_invocation_id) localId: vec3u) {
    let lane = localId.x;
    let localIndex = groupId.x * ${I}u + lane;
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

    var stride = ${I/2}u;
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
        let outputIndex = (params.outputBucket + groupId.x) * ${K}u;
        if (valid[0] == 0u || nanRunCounts[0] >= 2u) {
            let nan = source[nanIndices[0]];
            let point = vec2f(f32(params.globalOffset + nanIndices[0]) / ${I}.0, nan);
            output[outputIndex] = point;
            output[outputIndex + 1u] = point;
            output[outputIndex + 2u] = point;
        } else {
            var firstIndex = minimumIndices[0];
            var secondIndex = maximumIndices[0];
            var firstPoint = vec2f(f32(params.globalOffset + firstIndex) / ${I}.0, minimums[0]);
            var secondPoint = vec2f(f32(params.globalOffset + secondIndex) / ${I}.0, maximums[0]);
            if (secondIndex < firstIndex) {
                let swapIndex = firstIndex; firstIndex = secondIndex; secondIndex = swapIndex;
                let swapPoint = firstPoint; firstPoint = secondPoint; secondPoint = swapPoint;
            }
            var thirdIndex = secondIndex;
            var thirdPoint = secondPoint;
            if (nanSeen[0] != 0u) {
                thirdIndex = nanIndices[0];
                thirdPoint = vec2f(f32(params.globalOffset + thirdIndex) / ${I}.0, source[thirdIndex]);
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
`,ee=`
struct Params {
    first: u32,
    visibleLength: u32,
    bucketCount: u32,
    sourceLength: u32,
};
@group(0) @binding(0) var<storage, read> source: array<vec2f>;
@group(0) @binding(1) var<storage, read_write> output: array<vec2f>;
@group(0) @binding(2) var<uniform> params: Params;
var<workgroup> minimums: array<vec2f, ${m}>;
var<workgroup> maximums: array<vec2f, ${m}>;
var<workgroup> valid: array<u32, ${m}>;
var<workgroup> nanSeen: array<u32, ${m}>;
var<workgroup> minimumIndices: array<u32, ${m}>;
var<workgroup> maximumIndices: array<u32, ${m}>;
var<workgroup> nanIndices: array<u32, ${m}>;
var<workgroup> nanRunCounts: array<u32, ${m}>;
fn isNonFinite(x: f32) -> bool { let b = bitcast<u32>(x); return (b & 0x7f800000u) == 0x7f800000u; }
@compute @workgroup_size(${m})
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
        index += ${m}u;
    }
    minimums[lane] = minimum; maximums[lane] = maximum; valid[lane] = hasValue; nanSeen[lane] = hasNan;
    minimumIndices[lane] = minimumIndex; maximumIndices[lane] = maximumIndex; nanIndices[lane] = nanIndex;
    nanRunCounts[lane] = nanRunCount;
    workgroupBarrier();
    var stride = ${m/2}u;
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
        let outIndex = bucket * ${K}u + 1u;
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
`,xe=`
struct Params {
    first: u32,
    visibleLength: u32,
    bucketCount: u32,
    sourceLength: u32,
};

@group(0) @binding(0) var<storage, read> source: array<f32>;
@group(0) @binding(1) var<storage, read_write> output: array<vec2f>;
@group(0) @binding(2) var<uniform> params: Params;

var<workgroup> minimums: array<f32, ${m}>;
var<workgroup> maximums: array<f32, ${m}>;
var<workgroup> minimumIndices: array<u32, ${m}>;
var<workgroup> maximumIndices: array<u32, ${m}>;
var<workgroup> valid: array<u32, ${m}>;
var<workgroup> nanSeen: array<u32, ${m}>;
var<workgroup> nanIndices: array<u32, ${m}>;
var<workgroup> nanRunCounts: array<u32, ${m}>;

fn isNonFinite(x: f32) -> bool {
    let bits = bitcast<u32>(x);
    return (bits & 0x7f800000u) == 0x7f800000u;
}

@compute @workgroup_size(${m})
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

        index += ${m}u;
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

    var stride = ${m/2}u;
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
        let outputIndex = bucket * ${K}u + 1u;

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
`,l=`
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

var<workgroup> minimums: array<f32, ${$}>;
var<workgroup> maximums: array<f32, ${$}>;
var<workgroup> valid: array<u32, ${$}>;

fn isFiniteValue(x: f32) -> bool {
    let bits = bitcast<u32>(x);
    return (bits & 0x7f800000u) != 0x7f800000u;
}

@compute @workgroup_size(${$})
fn reduceRange(
    @builtin(workgroup_id) workgroupId: vec3u,
    @builtin(local_invocation_id) localId: vec3u) {
    let group = workgroupId.x;
    let lane = localId.x;
    let invocationCount = params.workgroupCount * ${$}u;
    var index = group * ${$}u + lane;
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

    var stride = ${$/2}u;
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
`;Object.assign(s,{uniformBufferSize:R,fillVerticesPerSegment:g,lineVerticesPerSegment:W,decimationWorkgroupSize:m,decimationFactor:x,decimationBucketsPerPixel:_,maxDecimationBuckets:F,rangeWorkgroupSize:$,maxRangeWorkgroups:se,defaultCacheBudget:ne,overviewBucketSize:I,reducedPointsPerBucket:K,streamChunkLength:ce,rawChunkLength:Y,shader:Z,overviewShader:A,pointDecimationShader:ee,decimationShader:xe,rangeShader:l})})(),(function(){const s=window.__nexusChartWebGpu,{shader:R,decimationShader:g,rangeShader:W,overviewShader:m,pointDecimationShader:x,defaultCacheBudget:_,reducedPointsPerBucket:F}=s,$=new Map,se=new Map,ne=new Map,I=new Map,K=new Map,ce=new Map;let Y=null,Z=null,A=0;function ee(e,t){if(!e)return;const n=t.charAt(0).toLowerCase()+t.slice(1);return e[t]??e[n]}function xe(e){const t=ee(e,"Color")??{};return[(ee(t,"Red")??0)/255,(ee(t,"Green")??0)/255,(ee(t,"Blue")??0)/255,(ee(t,"Alpha")??255)/255]}function l(e){const t=window.devicePixelRatio||1,n=Math.max(1,Math.round(e.clientWidth*t)),r=Math.max(1,Math.round(e.clientHeight*t));return(e.width!==n||e.height!==r)&&(e.width=n,e.height=r),{width:n,height:r,dpr:t}}function b(e,t,n){const r=e.canvasContexts.get(t);if(r?.canvas===n)return r.context;r?.context.unconfigure?.(),e.previewRenderKeys.delete(t);const o=n.getContext("webgpu");if(!o)throw new Error("The browser could not create a WebGPU canvas context.");return o.configure({device:e.device,format:e.format,alphaMode:"premultiplied"}),e.canvasContexts.set(t,{canvas:n,context:o}),o}function P(e,t){e.canvasContexts.get(t)?.context.unconfigure?.(),e.canvasContexts.delete(t),e.previewRenderKeys.delete(t)}function G(e){return e*F+2}function N(e,t){!t||t.__nexusDestroyed||(t.__nexusDestroyed=!0,e.ownedGpuBytes-=t.__nexusByteLength??0,t.destroy())}function c(e,t,n=new Set,r=e.cacheBudget){if(s.evictRawChunks?.(e,t,n,r),e.ownedGpuBytes+e.rawReservedBytes+t>r)throw new Error(`Chart GPU memory budget (${r} bytes) cannot fit a ${t}-byte allocation`)}function y(e,t,n){c(e,t.size,n);const r=e.device.createBuffer(t);return r.__nexusByteLength=t.size,r.__nexusDestroyed=!1,e.ownedGpuBytes+=t.size,r}function T(e){return ne.get(e)??0}function w(e){const t=T(e)+1;return ne.set(e,t),t}function k(e){const t=new Error(e);return t.webGpuCancelled=!0,t}function B(e){return e?.webGpuCancelled===!0}function L(e,t,n){const r=I.get(e);if(r?.title===t&&r?.message===n)return;I.set(e,{title:t,message:n}),K.get(e)?.invokeMethodAsync("WebGpuFailed",t,n).catch(d=>console.error("[chart-webgpu] failure callback failed",d))}function C(e,t,n,r){if(T(e)!==t||I.has(e))return;w(e),se.delete(e);const o=$.get(e);$.delete(e),o&&U(o,`${n}: ${r}`),j(),L(e,n,r)}function U(e,t){if(!(!e||e.disposed)){e.disposed=!0,s.invalidateChartRenders?.(e.chartId);for(const n of e.rawRequests.values())e.rawReservedBytes-=n.byteLength,n.reject(k(t));e.rawRequests.clear();for(const[n,r]of e.rawChunks)s.destroyRawChunk(e,n,r);for(const n of e.seriesBuffers.values())s.destroySeriesBuffer(e,n);e.seriesBuffers.clear();for(const n of e.chunkedUploadSessions.values())s.destroyChunkedUpload(e,n);e.chunkedUploadSessions.clear();for(const n of e.targetResources.values())for(const r of n)N(e,r.uniformBuffer);e.targetResources.clear();for(const{context:n}of e.canvasContexts.values())n.unconfigure?.();e.canvasContexts.clear(),e.lastPayloads.clear(),e.previewRenderKeys.clear()}}function j(){if($.size>0||se.size>0||(A++,!Y))return;const e=Y;Y=null,e.alive=!1,e.device.destroy()}async function me(){if(Y)return Y;if(Z)return Z;const e=++A;return Z=Promise.resolve().then(async()=>{if(!navigator.gpu)throw new Error("WebGPU is not available. Use a current WebGPU-capable browser and ensure hardware acceleration is enabled.");const t=await navigator.gpu.requestAdapter();if(!t)throw new Error("No compatible GPU adapter was found. Ensure hardware acceleration is enabled, then retry.");const n=await t.requestDevice({requiredLimits:{maxBufferSize:t.limits.maxBufferSize,maxStorageBufferBindingSize:t.limits.maxStorageBufferBindingSize}});let r;try{if(e!==A)throw new Error("WebGPU initialization was superseded.");const o=navigator.gpu.getPreferredCanvasFormat(),d=n.createShaderModule({code:R}),p=n.createShaderModule({code:g}),h=n.createShaderModule({code:W}),O=n.createShaderModule({code:m}),V=n.createShaderModule({code:x});r={generation:e,alive:!0,device:n,format:o,pipeline:n.createRenderPipeline({layout:"auto",vertex:{module:d,entryPoint:"vertexMain"},fragment:{module:d,entryPoint:"fragmentMain",targets:[{format:o,blend:{color:{srcFactor:"src-alpha",dstFactor:"one-minus-src-alpha",operation:"add"},alpha:{srcFactor:"one",dstFactor:"one-minus-src-alpha",operation:"add"}}}]},primitive:{topology:"triangle-list"}}),decimationPipeline:n.createComputePipeline({layout:"auto",compute:{module:p,entryPoint:"decimate"}}),rangePipeline:n.createComputePipeline({layout:"auto",compute:{module:h,entryPoint:"reduceRange"}}),overviewPipeline:n.createComputePipeline({layout:"auto",compute:{module:O,entryPoint:"reduceOverview"}}),pointDecimationPipeline:n.createComputePipeline({layout:"auto",compute:{module:V,entryPoint:"decimatePoints"}})}}catch(o){throw n.destroy(),o}return n.lost.then(o=>{r.alive=!1,Y===r&&(Y=null,A++);const d=o.message?` ${o.message}`:"";for(const[p,h]of[...$])h.gpuGeneration===r.generation&&(w(p),$.delete(p),U(h,`WebGPU device lost for chart ${p}`),L(p,"GPU connection lost",`The browser lost access to the GPU.${d} Retry the chart to recreate its GPU resources.`))}).catch(o=>console.error("[chart-webgpu] device loss handler failed",o)),Y=r,r}).finally(()=>{Z=null}),Z}async function Se(e){if(!K.has(e))throw k(`Chart ${e} is no longer active`);let t=$.get(e);if(t)return t;const n=I.get(e);if(n)throw new Error(n.message);let r=se.get(e);if(r)return r.promise;const o=T(e),d={epoch:o,promise:null};return d.promise=Promise.resolve().then(async()=>{try{if(T(e)!==o)return null;if(!document.getElementById(`series_${e}`))throw new Error("The chart canvas is unavailable.");const h=await me();if(T(e)!==o)return null;if(!h.alive||Y!==h||h.generation!==A)throw new Error("The WebGPU device was lost during chart initialization.");return t={chartId:e,...h,gpuGeneration:h.generation,seriesBuffers:new Map,chunkedUploadSessions:new Map,uploadToken:0,targetResources:new Map,canvasContexts:new Map,previewRenderKeys:new Map,uploadGenerations:new Map,cacheBudget:ce.get(e)??_,ownedGpuBytes:0,rawReservedBytes:0,rawChunks:new Map,rawRequests:new Map,workerRequestId:0,lastPayloads:new Map,disposed:!1},T(e)!==o?(U(t,`Chart ${e} initialization was superseded`),null):($.set(e,t),t)}catch(p){if(!I.has(e)&&T(e)===o){const h=!navigator.gpu||p?.message?.startsWith("No compatible GPU adapter");L(e,h?"WebGPU unavailable":"WebGPU initialization failed",h?p.message:`The chart could not initialize WebGPU: ${p?.message??p}. Check browser hardware acceleration, then retry.`)}throw p}finally{se.get(e)===d&&se.delete(e),!K.has(e)&&!$.has(e)&&ne.delete(e)}}),se.set(e,d),d.promise}Object.assign(s,{instances:$,pendingInstances:se,lifecycleEpochs:ne,failureStates:I,dotNetHelpers:K,configuredCacheBudgets:ce,valueOf:ee,colorOf:xe,ensureCanvasSize:l,getCanvasContext:b,releaseCanvasContext:P,getReducedOutputLength:G,createTrackedBuffer:y,destroyTrackedBuffer:N,ensureGpuCapacity:c,getLifecycleEpoch:T,advanceLifecycleEpoch:w,cancellationError:k,isCancellationError:B,reportFailure:L,reportRuntimeFailure:C,destroyInstance:U,releaseSharedGpuIfUnused:j,getSharedGpu:me,getInstance:Se})})(),(function(){const s=window.__nexusChartWebGpu,{instances:R,dotNetHelpers:g,getInstance:W,valueOf:m,overviewBucketSize:x,reducedPointsPerBucket:_,streamChunkLength:F,rawChunkLength:$,rangeWorkgroupSize:se,maxRangeWorkgroups:ne}=s;function I(e,t,n){return`${e}:${t}:${n}`}function K(e,t){s.destroyTrackedBuffer(e,t.buffer),t.pointBuffer!==t.buffer&&s.destroyTrackedBuffer(e,t.pointBuffer);for(const n of t.decimations?.values()??[])s.destroyTrackedBuffer(e,n.outputBuffer),s.destroyTrackedBuffer(e,n.paramsBuffer)}function ce(e,t,n){K(e,n),e.rawChunks.delete(t)}function Y(e,t,n){e.rawRequests.get(t)===n&&e.rawRequests.delete(t),n.reservationActive&&(e.rawReservedBytes-=n.byteLength,n.reservationActive=!1),s.destroyTrackedBuffer(e,n.buffer)}function Z(e,t,n,r,o){n.buffer=null;const d={id:n.id,buffer:r,pointBuffer:r,dataMode:0,decimations:new Map,offset:n.offset,length:o,byteLength:n.byteLength,lastUsed:performance.now()};e.rawChunks.set(t,d),Y(e,t,n),A(e,0),n.resolve(d),me(e)}function A(e,t,n=new Set,r=e.cacheBudget){const o=[...e.rawChunks.entries()].filter(([d])=>!n.has(d)).sort((d,p)=>d[1].lastUsed-p[1].lastUsed);for(;e.ownedGpuBytes+e.rawReservedBytes+t>r&&o.length;){const[d,p]=o.shift();ce(e,d,p)}if(t>0&&e.ownedGpuBytes+e.rawReservedBytes+t>r){const d=new Error(`Chart GPU memory budget (${r} bytes) cannot fit a ${t}-byte allocation`);throw d.webGpuCacheCapacity=!0,d}}function ee(e,t){for(const[n,r]of e.rawRequests)r.id===t&&(r.reject(s.cancellationError(`Raw chunk request superseded for series ${t}`)),Y(e,n,r));for(const[n,r]of e.rawChunks)r.id===t&&ce(e,n,r)}function xe(e,t){const n=new Set(t);for(const[r,o]of e.seriesBuffers)n.has(o.id)||(K(e,o),e.seriesBuffers.delete(r),ee(e,o.id),e.uploadGenerations.delete(o.id));for(const[r,o]of e.chunkedUploadSessions)n.has(o.id)||(l(e,o),e.chunkedUploadSessions.delete(r))}function l(e,t){s.destroyTrackedBuffer(e,t.transientBuffer),s.destroyTrackedBuffer(e,t.overviewBuffer),s.destroyTrackedBuffer(e,t.paramsBuffer)}async function b(e,t,n,r,o,d){const p=await L(e,t,d);e.device.queue.writeBuffer(n,0,new Uint32Array([o,d,Math.floor(o/x),0]));const h=e.device.createCommandEncoder(),O=h.beginComputePass();return O.setPipeline(e.overviewPipeline),O.setBindGroup(0,r),O.dispatchWorkgroups(Math.ceil(d/x)),O.end(),e.device.queue.submit([h.finish()]),await e.device.queue.onSubmittedWorkDone(),p}async function P(e,t,n,r){const o=await W(e);if(!o)throw new Error(`WebGPU instance unavailable for chart ${e}`);if(!Number.isSafeInteger(r)||r<2)throw new Error(`Chunked series length must be a safe integer of at least 2 (received ${r})`);const d=Math.ceil(r/x),p=d*_,h=p*2*Float32Array.BYTES_PER_ELEMENT,O=Math.min(F,r)*Float32Array.BYTES_PER_ELEMENT,V=Math.min(o.device.limits.maxBufferSize,o.device.limits.maxStorageBufferBindingSize);if(h>V)throw new Error(`Persistent overview requires ${h} bytes, exceeding the GPU storage buffer limit of ${V} bytes`);if(O>V)throw new Error(`Series stream chunk requires ${O} bytes, exceeding the GPU storage buffer limit of ${V} bytes`);const Q=(o.uploadGenerations.get(t)??0)+1;o.uploadGenerations.set(t,Q),ee(o,t);for(const[J,de]of o.chunkedUploadSessions)de.id===t&&(l(o,de),o.chunkedUploadSessions.delete(J));const te=++o.uploadToken;let ie=null,re=null,oe=null;try{ie=s.createTrackedBuffer(o,{size:O,usage:GPUBufferUsage.STORAGE|GPUBufferUsage.COPY_DST}),re=s.createTrackedBuffer(o,{size:h,usage:GPUBufferUsage.STORAGE}),oe=s.createTrackedBuffer(o,{size:16,usage:GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST});const J=o.device.createBindGroup({layout:o.overviewPipeline.getBindGroupLayout(0),entries:[{binding:0,resource:{buffer:ie}},{binding:1,resource:{buffer:re}},{binding:2,resource:{buffer:oe}}]});return o.chunkedUploadSessions.set(te,{id:t,version:n,length:r,generation:Q,overviewLength:p,overviewBucketCount:d,transientBuffer:ie,overviewBuffer:re,paramsBuffer:oe,bindGroup:J,writtenLength:0,rangeHasValue:!1,rangeMinimum:0,rangeMaximum:0}),te}catch(J){throw s.destroyTrackedBuffer(o,ie),s.destroyTrackedBuffer(o,re),s.destroyTrackedBuffer(o,oe),J}}function G(e,t,n){const r=e??t;if(!Number.isSafeInteger(r)||r<0||r>t)throw new Error(`Chunk ${n} length ${r} exceeds payload ${n} length ${t}`);return r}function N(e,t){if(e?._unsafe_create_view){const n=e._unsafe_create_view(),r=G(t,n.byteLength,"byte");if(r%Float32Array.BYTES_PER_ELEMENT!==0)throw new Error(`Chunk byte length ${r} is not aligned to float size`);return n instanceof Float32Array?r===n.byteLength?n:n.subarray(0,r/Float32Array.BYTES_PER_ELEMENT):new Float32Array(n.buffer,n.byteOffset,r/Float32Array.BYTES_PER_ELEMENT)}if(e?.getUint8Array){const n=e.getUint8Array(),r=G(t,n.byteLength,"byte");if(r%Float32Array.BYTES_PER_ELEMENT!==0)throw new Error(`Chunk byte length ${r} is not aligned to float size`);return new Float32Array(n.buffer,n.byteOffset,r/Float32Array.BYTES_PER_ELEMENT)}if(e?.getFloat32Array){const n=e.getFloat32Array(),r=G(t,n.length,"sample");return r===n.length?n:n.subarray(0,r)}if(e instanceof Float32Array){const n=G(t,e.length,"sample");return n===e.length?e:e.subarray(0,n)}if(ArrayBuffer.isView(e)){if(e instanceof Uint8Array||e instanceof Int8Array||e instanceof Uint8ClampedArray){const o=G(t,e.byteLength,"byte");if(o%Float32Array.BYTES_PER_ELEMENT!==0)throw new Error(`Chunk byte length ${o} is not aligned to float size`);return new Float32Array(e.buffer,e.byteOffset,o/Float32Array.BYTES_PER_ELEMENT)}const n=Float32Array.from(e),r=G(t,n.length,"sample");return r===n.length?n:n.subarray(0,r)}if(e instanceof ArrayBuffer){const n=G(t,e.byteLength,"byte");if(n%Float32Array.BYTES_PER_ELEMENT!==0)throw new Error(`Chunk byte length ${n} is not aligned to float size`);return new Float32Array(e,0,n/Float32Array.BYTES_PER_ELEMENT)}if(Array.isArray(e)){const n=Float32Array.from(e),r=G(t,n.length,"sample");return r===n.length?n:n.subarray(0,r)}throw new Error("Synchronous chunk upload requires a MemoryView, typed array, ArrayBuffer, or array payload")}function c(e,t,n,r,o){const d=R.get(e),p=d?.chunkedUploadSessions.get(t);if(!p)throw s.cancellationError(`Chunked series upload ${t} is no longer active`);const h=N(r,o),O=h.length;if(n!==p.writtenLength||n+O>p.length)throw new Error(`Chunked series upload ${t} expected sample offset ${p.writtenLength}, received ${n}`);d.device.queue.writeBuffer(p.transientBuffer,0,h)}async function y(e,t,n,r){const o=await W(e),d=o?.chunkedUploadSessions.get(t);if(!d)throw s.cancellationError(`Chunked series upload ${t} is no longer active`);if(n!==d.writtenLength||n+r>d.length)throw new Error(`Chunked series upload ${t} expected sample offset ${d.writtenLength}, received ${n}`);const p=await b(o,d.transientBuffer,d.paramsBuffer,d.bindGroup,n,r);if(R.get(e)!==o||o.chunkedUploadSessions.get(t)!==d)throw s.cancellationError(`Chunked series upload ${t} was superseded`);p.hasValue&&(d.rangeMinimum=d.rangeHasValue?Math.min(d.rangeMinimum,p.minimum):p.minimum,d.rangeMaximum=d.rangeHasValue?Math.max(d.rangeMaximum,p.maximum):p.maximum,d.rangeHasValue=!0),d.writtenLength+=r}async function T(e,t){const n=await W(e),r=n?.chunkedUploadSessions.get(t);if(!r)throw s.cancellationError(`Chunked series upload ${t} is no longer active`);if(r.writtenLength!==r.length)throw new Error(`Chunked series upload ${t} is incomplete`);for(const[o,d]of n.seriesBuffers)d.id===r.id&&(K(n,d),n.seriesBuffers.delete(o));return n.seriesBuffers.set(I(r.id,r.version,r.length),{id:r.id,version:r.version,length:r.length,buffer:r.overviewBuffer,pointBuffer:r.overviewBuffer,overviewLength:r.overviewLength,overviewBucketCount:r.overviewBucketCount,dataMode:1,chunked:!0,decimations:new Map}),s.destroyTrackedBuffer(n,r.transientBuffer),s.destroyTrackedBuffer(n,r.paramsBuffer),n.chunkedUploadSessions.delete(t),{hasValue:r.rangeHasValue,minimum:r.rangeMinimum,maximum:r.rangeMaximum}}function w(e,t){const n=R.get(e),r=n?.chunkedUploadSessions.get(t);r&&(l(n,r),n.chunkedUploadSessions.delete(t))}function k(e,t){const n=m(t,"Id"),r=m(t,"DataVersion")??0,o=m(t,"Length")??0;return o<2?null:e.seriesBuffers.get(I(n,r,o))??null}function B(e,t,n,r){const o=m(t,"Zoom")??{},p=(m(t,"Series")??[]).map(h=>{const O=m(h,"Id"),V=m(h,"DataVersion")??0,Q=m(h,"Length")??0,te=m(h,"Color")??{};return[O,V,Q,m(h,"SampleStep"),e.seriesBuffers.has(I(O,V,Q)),m(h,"OverviewAxisMin"),m(h,"OverviewAxisMax"),m(te,"Red"),m(te,"Green"),m(te,"Blue"),m(te,"Alpha")]});return JSON.stringify([n,r,m(o,"Left")??0,m(o,"Right")??1,m(t,"LineWidth")??.7,m(t,"FillOpacity")??.1,p])}async function L(e,t,n){const r=Math.min(ne,Math.max(1,Math.ceil(n/se))),o=r*16;let d=null,p=null,h=null;try{d=s.createTrackedBuffer(e,{size:o,usage:GPUBufferUsage.STORAGE|GPUBufferUsage.COPY_SRC}),p=s.createTrackedBuffer(e,{size:o,usage:GPUBufferUsage.COPY_DST|GPUBufferUsage.MAP_READ}),h=s.createTrackedBuffer(e,{size:16,usage:GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST}),e.device.queue.writeBuffer(h,0,new Uint32Array([n,r,0,0]));const O=e.device.createBindGroup({layout:e.rangePipeline.getBindGroupLayout(0),entries:[{binding:0,resource:{buffer:t}},{binding:1,resource:{buffer:d}},{binding:2,resource:{buffer:h}}]}),V=e.device.createCommandEncoder(),Q=V.beginComputePass();Q.setPipeline(e.rangePipeline),Q.setBindGroup(0,O),Q.dispatchWorkgroups(r),Q.end(),V.copyBufferToBuffer(d,0,p,0,o),e.device.queue.submit([V.finish()]),await p.mapAsync(GPUMapMode.READ);const te=new DataView(p.getMappedRange());let ie=0,re=0,oe=!1;for(let J=0;J<r;J++){const de=J*16;if(te.getUint32(de+8,!0)===0)continue;const q=te.getFloat32(de,!0),M=te.getFloat32(de+4,!0);if(!Number.isFinite(q)||!Number.isFinite(M))throw new Error(`WebGPU range reduction returned non-finite values for workgroup ${J}`);ie=oe?Math.min(ie,q):q,re=oe?Math.max(re,M):M,oe=!0}return{hasValue:oe,minimum:ie,maximum:re}}finally{p?.mapState==="mapped"&&p.unmap(),s.destroyTrackedBuffer(e,d),s.destroyTrackedBuffer(e,p),s.destroyTrackedBuffer(e,h)}}function C(e,t){return`${e.id}:${e.version}:${t}`}function U(e,t,n,r){const o=C(t,n),d=e.rawChunks.get(o);if(d)return d.lastUsed=performance.now(),Promise.resolve(d);const p=e.rawRequests.get(o);if(p)return p.promise;const h=n*$;if(h>=t.length)return Promise.resolve(null);const O=Math.min($+(h+$<t.length?1:0),t.length-h),V=O*Float32Array.BYTES_PER_ELEMENT;A(e,V,r),e.rawReservedBytes+=V;const Q=++e.workerRequestId;let te,ie;const re=new Promise((M,ve)=>{te=M,ie=ve}),oe=s.createTrackedBuffer(e,{size:V,usage:GPUBufferUsage.STORAGE|GPUBufferUsage.COPY_DST}),J={id:t.id,requestId:Q,promise:re,resolve:te,reject:ie,byteLength:V,buffer:oe,offset:h,count:O,writtenLength:0,reservationActive:!0};e.rawRequests.set(o,J);const de={onerror:M=>{Y(e,o,J),ie(new Error(`Raw chunk ${n} generation failed: ${M.message}`))}},q=g.get(t.chartId);return q?q.invokeMethodAsync("ProvideSeriesChunk",t.id,h,O,Q).catch(M=>de.onerror({message:M?.message??M})):de.onerror({message:`Chart ${t.chartId} is no longer active`}),re}function j(e,t,n,r,o){const d=R.get(e),p=[...d?.rawRequests.values()??[]].find(V=>V.requestId===t);if(!p)throw s.cancellationError(`Raw chunk request ${t} is no longer active`);const h=N(r,o),O=n*Float32Array.BYTES_PER_ELEMENT;if(!p.buffer)throw new Error(`Raw chunk request ${t} has no destination buffer`);if(n!==p.writtenLength||n+h.length>p.count)throw new Error(`Raw chunk request ${t} expected sample offset ${p.writtenLength}, received ${n}`);if(d.device.queue.writeBuffer(p.buffer,O,h),p.writtenLength+=h.length,p.writtenLength===p.count){const V=[...d.rawRequests.entries()].find(([,Q])=>Q===p)?.[0];if(!V)throw s.cancellationError(`Raw chunk request ${t} was removed before completion`);Z(d,V,p,p.buffer,p.count)}}function me(e){for(const[t,n]of e.lastPayloads)e.previewRenderKeys.delete(t),s.scheduleRender(e.chartId,n).catch(r=>{s.isCancellationError(r)||console.error("[chart-webgpu] raw rerender failed",r)})}function Se(e,t,n,r,o,d,p,h){const O=s.getTimeWindow(r,n,t.length);if(!O)return null;const{first:V,last:Q,indexLeft:te,indexRange:ie}=O,re=Q-V;if(!Number.isFinite(re)||re<=0||re>$*2)return null;const oe=Math.floor(V/$),J=Math.floor((Q-1)/$);for(let q=oe;q<=J;q++)h.add(C(t,q));for(let q=oe;q<=J;q++)try{U(e,t,q,h).catch(M=>{s.isCancellationError(M)||(s.reportRuntimeFailure(t.chartId,t.lifecycleEpoch,"WebGPU data generation failed",`${M?.message??M} Retry the chart to recreate its GPU resources.`),console.error("[chart-webgpu] raw request failed",M))})}catch(M){if(!M?.webGpuCacheCapacity)throw M}for(const q of[oe-1,J+1])if(!(q<0||q>=Math.ceil(t.length/$)))try{U(e,t,q,h).catch(M=>{s.isCancellationError(M)||console.error("[chart-webgpu] raw prefetch failed",M)})}catch(M){s.isCancellationError(M)||console.error("[chart-webgpu] raw prefetch skipped",M)}const de=[];for(let q=oe;q<=J;q++){const M=e.rawChunks.get(C(t,q));if(!M)return null;M.lastUsed=performance.now();const ve=Math.max(0,Math.floor(V-M.offset)),we=Math.min(M.length-1,Math.ceil(Q-M.offset));if(we<=ve)continue;const Re={first:ve,segmentCount:we-ve,zoomedLeft:o.plotLeft+(M.offset+ve-te)/ie*o.plotWidth,dx:o.plotWidth/ie};de.push(s.getRenderBuffer(e,M,Re,o,d,`${p}:${t.id}:${q}`,h))}return de.length?de:null}Object.assign(s,{getSeriesKey:I,destroySeriesBuffer:K,destroyRawChunk:ce,evictRawChunks:A,removeRawSeries:ee,synchronizeSeries:xe,destroyChunkedUpload:l,beginChunkedSeriesAsync:P,appendChunkedSeries:c,processChunkedSeriesUploadAsync:y,appendSeriesChunk:j,completeChunkedSeriesAsync:T,abortChunkedSeries:w,getSeriesBuffer:k,getPreviewRenderKey:B,calculateSeriesRangeAsync:L,rawChunkKey:C,requestRawChunk:U,rerenderLastPayloads:me,getRawRenderItems:Se})})(),(function(){const s=window.__nexusChartWebGpu,{instances:R,pendingInstances:g,lifecycleEpochs:W,failureStates:m,dotNetHelpers:x,configuredCacheBudgets:_,valueOf:F,colorOf:$,ensureCanvasSize:se,getCanvasContext:ne,releaseCanvasContext:I,getReducedOutputLength:K,getSharedGpu:ce,getInstance:Y,getLifecycleEpoch:Z,advanceLifecycleEpoch:A,isCancellationError:ee,reportRuntimeFailure:xe,destroyInstance:l,releaseSharedGpuIfUnused:b,evictRawChunks:P,createTrackedBuffer:G,destroyTrackedBuffer:N,synchronizeSeries:c,beginChunkedSeriesAsync:y,appendChunkedSeries:T,processChunkedSeriesUploadAsync:w,completeChunkedSeriesAsync:k,abortChunkedSeries:B,appendSeriesChunk:L,getSeriesBuffer:C,getPreviewRenderKey:U,getRawRenderItems:j,uniformBufferSize:me,fillVerticesPerSegment:Se,lineVerticesPerSegment:e,decimationFactor:t,decimationBucketsPerPixel:n,maxDecimationBuckets:r,overviewBucketSize:o,reducedPointsPerBucket:d,rawChunkLength:p}=s,h=new Map;function O(i,a){return`${i}:${a}`}function V(i){for(const[a,f]of h)a.startsWith(`${i}:`)&&(f.generation++,f.pending=null,h.delete(a))}function Q(i,a){for(const f of[...i.seriesBuffers.values(),...i.rawChunks.values()])for(const[v,u]of f.decimations??[])v!==a&&!v.startsWith(`${a}:`)||(N(i,u.outputBuffer),N(i,u.paramsBuffer),f.decimations.delete(v))}function te(i,a){const f=O(i,a),v=h.get(f);v&&(v.generation++,v.pending=null,h.delete(f));const u=R.get(i);u&&(u.lastPayloads.delete(a),u.previewRenderKeys.delete(a),re(u,a,0),I(u,a),Q(u,a))}function ie(i,a,f,v,u){let S=i.targetResources.get(v);S||(S=[],i.targetResources.set(v,S));let E=S[f];if(E?.seriesBuffer===a)return E;N(i,E?.uniformBuffer);const z=G(i,{size:me,usage:GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST},u),H=i.device.createBindGroup({layout:i.pipeline.getBindGroupLayout(0),entries:[{binding:0,resource:{buffer:z}},{binding:1,resource:{buffer:a.buffer}},{binding:2,resource:{buffer:a.pointBuffer}}]});return E={seriesBuffer:a,uniformBuffer:z,bindGroup:H},S[f]=E,E}function re(i,a,f){const v=i.targetResources.get(a);if(!(!v||v.length<=f)){for(const u of v.splice(f))N(i,u.uniformBuffer);v.length===0&&i.targetResources.delete(a)}}function oe(i,a,f){const v=F(i,"Plot")??{},u=(F(v,"Left")??0)*a,S=(F(v,"Top")??0)*f,E=(F(v,"Right")??1)*a,z=(F(v,"Bottom")??1)*f,H=E-u,D=z-S;return H<=0||D<=0?null:{plotLeft:u,plotTop:S,plotRight:E,plotBottom:z,plotWidth:H,plotHeight:D}}function J(i,a,f){const v=F(i,"Zoom")??{},u=F(v,"Left")??0,S=F(v,"Right")??1,E=F(a,"SampleStep"),z=u/E,H=S/E,D=H-z;if(!Number.isFinite(E)||E<=0||!Number.isFinite(D)||D<=0)return null;const ue=Math.max(0,Math.floor(z)),ae=Math.min(f-1,Math.ceil(H));return ae<ue?null:{first:ue,last:ae,indexLeft:z,indexRange:D}}function de(i,a,f,v){const u=J(i,a,f);if(!u)return null;const{first:S,last:E,indexLeft:z,indexRange:H}=u,D=E-S+1,ue=v.plotLeft+(S-z)/H*v.plotWidth,ae=v.plotWidth/H;return D<2||!Number.isFinite(ae)||ae<=0?null:{first:S,segmentCount:D-1,zoomedLeft:ue,dx:ae}}function q(i,a,f,v){const u=J(i,a,f.length);if(!u)return null;const{first:S,last:E,indexLeft:z,indexRange:H}=u,D=Math.max(0,Math.floor(S/o)-1),ue=Math.min(f.overviewBucketCount-1,Math.ceil(E/o)),ae=D*d,pe=(ue-D+1)*d;return pe<2?null:{first:ae,segmentCount:pe-1,zoomedLeft:v.plotLeft,dx:v.plotWidth/(H/o),xOrigin:z/o}}function M(i,a,f,v,u,S,E){const z=f.segmentCount+1,H=Math.min(r,Math.max(2,Math.ceil(v.plotWidth*n)));if(z<=H*t)return{seriesBuffer:a,zoomInfo:f};let D=a.decimations.get(S);const ue=K(H),ae=ue*2*Float32Array.BYTES_PER_ELEMENT;if(!D||D.bucketCount!==H){D&&(N(i,D.outputBuffer),N(i,D.paramsBuffer));let ke=null,he=null;try{ke=G(i,{size:ae,usage:GPUBufferUsage.STORAGE},E),he=G(i,{size:16,usage:GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST},E)}catch(Pe){throw N(i,ke),N(i,he),Pe}const fe=i.device.createBindGroup({layout:(a.dataMode===1?i.pointDecimationPipeline:i.decimationPipeline).getBindGroupLayout(0),entries:[{binding:0,resource:{buffer:a.dataMode===1?a.pointBuffer:a.buffer}},{binding:1,resource:{buffer:ke}},{binding:2,resource:{buffer:he}}]});D={bucketCount:H,outputBuffer:ke,paramsBuffer:he,bindGroup:fe,renderBuffer:{buffer:a.buffer,pointBuffer:ke,length:ue,dataMode:1}},a.decimations.set(S,D)}i.device.queue.writeBuffer(D.paramsBuffer,0,new Uint32Array([f.first,z,H,a.overviewLength??a.length]));const pe=u.beginComputePass();return pe.setPipeline(a.dataMode===1?i.pointDecimationPipeline:i.decimationPipeline),pe.setBindGroup(0,D.bindGroup),pe.dispatchWorkgroups(H),pe.end(),{seriesBuffer:D.renderBuffer,zoomInfo:{first:0,segmentCount:ue-1,zoomedLeft:f.zoomedLeft,dx:f.dx,xOrigin:f.xOrigin??0}}}function ve(i,a,f,v,u,S,E,z,H,D,ue){const ae=F(v,"Preview")??!1,pe=ae?F(u,"OverviewAxisMin")??0:F(u,"AxisMin")??0,he=(ae?F(u,"OverviewAxisMax")??1:F(u,"AxisMax")??1)-pe;if(!Number.isFinite(he)||he===0)return!1;const fe=(F(v,"LineWidth")??.7)*D,Pe=F(v,"FillOpacity")??.1,Ce=Math.min(S.plotBottom,Math.max(S.plotTop,S.plotBottom-(0-pe)/he*S.plotHeight)),ye=$(u),Ie=new ArrayBuffer(me),X=new Float32Array(Ie),be=new Uint32Array(Ie);return X[0]=z,X[1]=H,X[4]=S.plotLeft,X[5]=S.plotTop,X[6]=S.plotRight,X[7]=S.plotBottom,X[8]=pe,X[9]=he,X[10]=E.zoomedLeft,X[11]=E.dx,X[12]=Ce,X[13]=fe,X[14]=Pe,X[16]=ye[0],X[17]=ye[1],X[18]=ye[2],X[19]=ye[3],be[20]=E.first,be[21]=ue,be[22]=f.dataMode??0,X[23]=E.xOrigin??0,i.device.queue.writeBuffer(a,0,Ie),!0}async function we(i,a,f){const v=Z(i);try{return await f()}catch(u){throw ee(u)||xe(i,v,a,`${u?.message??u} Retry the chart to recreate its GPU resources.`),u}}async function Re(i,a,f=null,v=0){const u=await Y(i);if(!u)return;const{device:S,pipeline:E}=u,z=F(a,"Target")??"series";if(f&&(f.generation!==v||h.get(O(i,z))!==f))throw s.cancellationError(`Rendering target ${z} was superseded`);const H=document.getElementById(`${z}_${i}`);if(!H){I(u,z);return}const D=ne(u,z,H),{width:ue,height:ae,dpr:pe}=se(H),ke=F(a,"Preview")??!1;u.lastPayloads.set(z,a);let he=null;if(ke&&(he=U(u,a,ue,ae),u.previewRenderKeys.get(z)===he))return;const fe=oe(a,ue,ae),Pe=S.createCommandEncoder(),Ce=[],ye=new Set;let Ie=0;if(fe){const be=F(a,"Series")??[];for(const ge of be){const le=C(u,ge);if(!le)continue;if(le.chartId=i,le.generation=u.uploadGenerations.get(le.id),le.lifecycleEpoch=Z(i),le.chunked){const Ee=j(u,le,ge,a,fe,Pe,z,ye);if(Ee){for(const Le of Ee)Ce.push({series:ge,...Le});continue}}const Be=le.chunked?q(a,ge,le,fe):de(a,ge,le.length,fe);if(!Be)continue;const $e=M(u,le,Be,fe,Pe,z,ye);Ce.push({series:ge,...$e})}}const X=Pe.beginRenderPass({colorAttachments:[{view:D.getCurrentTexture().createView(),loadOp:"clear",storeOp:"store",clearValue:{r:0,g:0,b:0,a:0}}]});if(fe){X.setPipeline(E),X.setScissorRect(Math.max(0,Math.floor(fe.plotLeft)),Math.max(0,Math.floor(fe.plotTop)),Math.max(1,Math.ceil(fe.plotWidth)),Math.max(1,Math.ceil(fe.plotHeight)));for(const{series:be,seriesBuffer:ge,zoomInfo:le}of Ce){const Be=ie(u,ge,Ie++,z,ye);ve(u,Be.uniformBuffer,ge,a,be,fe,le,ue,ae,pe,0)&&(X.setBindGroup(0,Be.bindGroup),X.draw(le.segmentCount*Se))}for(const{series:be,seriesBuffer:ge,zoomInfo:le}of Ce){const Be=ie(u,ge,Ie++,z,ye);ve(u,Be.uniformBuffer,ge,a,be,fe,le,ue,ae,pe,1)&&(X.setBindGroup(0,Be.bindGroup),X.draw(le.segmentCount*e))}}X.end(),S.queue.submit([Pe.finish()]),re(u,z,Ie),he!==null&&u.previewRenderKeys.set(z,he)}function Me(i,a){const f=F(a,"Target")??"series",v=O(i,f);let u=h.get(v);if(u||(u={generation:0,pending:null,running:!1},h.set(v,u)),u.pending=a,u.running)return u.promise;u.running=!0;const S=u.generation;return u.promise=(async()=>{try{for(;u.pending&&u.generation===S;){const E=u.pending;u.pending=null,await we(i,"WebGPU rendering failed",()=>Re(i,E,u,S))}}finally{u.running=!1,h.get(v)===u&&!u.pending&&h.delete(v)}})(),u.promise}Object.assign(s,{getRenderBuffer:M,getTimeWindow:J,runRuntimeOperation:we,renderSeriesAsync:Re,scheduleRender:Me,releaseTarget:te,invalidateChartRenders:V}),window.nexus??={},window.nexus.chartWebGpu={initialize(i,a){x.set(i,a);const f=m.get(i);if(f){a.invokeMethodAsync("WebGpuFailed",f.title,f.message).catch(v=>console.error("[chart-webgpu] failure callback failed",v));return}Y(i).catch(v=>console.error("[chart-webgpu] initialization failed",v))},setCacheBudget(i,a){if(!Number.isSafeInteger(a)||a<0)throw new Error(`Cache budget must be a non-negative safe integer (received ${a})`);_.set(i,a);const f=R.get(i);return f&&(f.cacheBudget=a,P(f,0,new Set,a)),!f||f.ownedGpuBytes+f.rawReservedBytes<=a},synchronizeSeries(i,a){const f=R.get(i);f&&c(f,a)},beginChunkedSeries(i,a,f,v){return we(i,"WebGPU upload failed",()=>y(i,a,f,v))},appendChunkedSeries(i,a,f,v,u){const S=Z(i);try{T(i,a,f,v,u)}catch(E){throw ee(E)||xe(i,S,"WebGPU upload failed",`${E?.message??E} Retry the chart to recreate its GPU resources.`),E}},processChunkedSeriesUpload(i,a,f,v){return we(i,"WebGPU upload failed",()=>w(i,a,f,v))},completeChunkedSeries(i,a){return we(i,"WebGPU upload failed",()=>k(i,a))},abortChunkedSeries(i,a){B(i,a)},appendSeriesChunk(i,a,f,v,u){L(i,a,f,v,u)},renderSeries(i,a){Me(i,a).catch(f=>{ee(f)||console.error("[chart-webgpu] render failed",f)})},releaseTarget:te,async retry(i){A(i),g.delete(i);const a=R.get(i);return a&&(R.delete(i),l(a,`Chart ${i} WebGPU retry`)),m.delete(i),await Y(i)!==null},dispose(i){const a=g.get(i);A(i),g.delete(i);const f=R.get(i);R.delete(i),f&&l(f,`Chart ${i} was disposed`),_.delete(i),m.delete(i),x.delete(i),a||W.delete(i),b()}},globalThis.__nexusChartWebGpuTestHooks&&Object.assign(globalThis.__nexusChartWebGpuTestHooks,{instances:R,pendingInstances:g,lifecycleEpochs:W,failureStates:m,getSharedGpu:ce,getInstance:Y,runRuntimeOperation:we,evictRawChunks:P,trimDrawResources:re,colorOf:$,getCanvasContext:ne,releaseCanvasContext:I,getReducedOutputLength:K,reducedPointsPerBucket:d,getTimeWindow:J,getZoomInfo:de,getOverviewZoom:q,getRawRenderItems:j,scheduleRender:Me,releaseTarget:te,renderStates:h,createTrackedBuffer:G,destroyTrackedBuffer:N,releaseSharedGpuIfUnused:b,rawChunkLength:p})})();
