nexus.chart = {};
nexus.chart.charts = {};

nexus.chart.resize = function (chartId, elementId, left, top, right, bottom) {

    let element = document
        .getElementById(`${elementId}_${chartId}`);

    element.style.left = `${left * 100}%`;
    element.style.top = `${top * 100}%`;
    element.style.width = `${(right - left) * 100}%`;
    element.style.height = `${(bottom - top) * 100}%`;
};

nexus.chart.setTextContent = function (chartId, elementId, text) {

    let element = document.getElementById(`${elementId}_${chartId}`);
    element.textContent = text;
};

nexus.chart.translate = function (chartId, elementId, left, top) {

    let element = document.getElementById(`${elementId}_${chartId}`);
    element.style.removeProperty("display")
    element.style.left = `${left * 100}%`;
    element.style.top = `${top * 100}%`;
};

nexus.chart.hide = function (chartId, elementId) {

    let element = document.getElementById(`${elementId}_${chartId}`);
    element.style.display = "none"
};

nexus.chart.toRelative = function (chartId, clientX, clientY) {
   
    let overlay = document
        .getElementById(`overlay_${chartId}`);
    
    let rect = overlay
        .getBoundingClientRect();
    
    let x = (clientX - rect.left) / rect.width;
    let y = (clientY - rect.top) / rect.height;

    x = Math.max(0, x)
    x = Math.min(1, x)

    y = Math.max(0, y)
    y = Math.min(1, y)

    return {
        "x": x,
        "y": y
    };
}

nexus.chart.initNavigator = function (chartId, dotNetHelper) {
    const track = document.getElementById(`navigator-track_${chartId}`);
    const win = document.getElementById(`navigator-window_${chartId}`);
    const handleL = document.getElementById(`navigator-handle-left_${chartId}`);
    const handleR = document.getElementById(`navigator-handle-right_${chartId}`);

    if (!track || !win || !handleL || !handleR)
        return;

    const MIN_WIDTH = 0.001;
    let drag = null;

    const clamp = (v, lo, hi) => Math.max(lo, Math.min(v, hi));

    function begin(mode, e) {
        e.preventDefault();
        e.stopPropagation();
        drag = {
            mode,
            rect: track.getBoundingClientRect(),
            startX: e.clientX,
            startLeft: parseFloat(win.dataset.left),
            startRight: parseFloat(win.dataset.right)
        };
        window.addEventListener("pointermove", onMove);
        window.addEventListener("pointerup", onUp);
    }

    function onMove(e) {
        if (!drag)
            return;

        e.preventDefault();
        const deltaPct = (e.clientX - drag.startX) / drag.rect.width;
        let newLeft, newRight;

        if (drag.mode === "left") {
            newLeft = clamp(drag.startLeft + deltaPct, 0, drag.startRight - MIN_WIDTH);
            newRight = drag.startRight;
        }
        else if (drag.mode === "right") {
            newRight = clamp(drag.startRight + deltaPct, drag.startLeft + MIN_WIDTH, 1);
            newLeft = drag.startLeft;
        }
        else {
            const w = drag.startRight - drag.startLeft;
            newLeft = clamp(drag.startLeft + deltaPct, 0, 1 - w);
            newRight = newLeft + w;
        }

        win.style.left = `${newLeft * 100}%`;
        win.style.width = `${(newRight - newLeft) * 100}%`;

        dotNetHelper.invokeMethodAsync("NavigatorZoom", newLeft, newRight);
    }

    function onUp() {
        drag = null;
        window.removeEventListener("pointermove", onMove);
        window.removeEventListener("pointerup", onUp);
    }

    handleL.addEventListener("pointerdown", e => begin("left", e));
    handleR.addEventListener("pointerdown", e => begin("right", e));
    win.addEventListener("pointerdown", e => begin("pan", e));
};