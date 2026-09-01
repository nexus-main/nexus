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

nexus.chart.initInteractions = function (chartId, dotNetHelper) {
    const overlay = document.getElementById(`overlay_${chartId}`);
    const selection = document.getElementById(`selection_${chartId}`);
    const disposers = [];
    const clamp = (value, minimum, maximum) => Math.max(minimum, Math.min(value, maximum));
    let invokePending = false;
    let pendingZoom = null;

    function invokeZoom(method, values) {
        pendingZoom = { method, values };

        if (invokePending)
            return;

        invokePending = true;
        requestAnimationFrame(async () => {
            const next = pendingZoom;
            pendingZoom = null;

            try {
                await dotNetHelper.invokeMethodAsync(next.method, ...next.values);
            } finally {
                invokePending = false;

                if (pendingZoom)
                    invokeZoom(pendingZoom.method, pendingZoom.values);
            }
        });
    }

    function listen(element, name, handler, options) {
        element.addEventListener(name, handler, options);
        disposers.push(() => element.removeEventListener(name, handler, options));
    }

    if (overlay && selection) {
        let drag = null;

        listen(overlay, "pointerdown", e => {
            if (e.button !== 0 && e.button !== 1)
                return;

            e.preventDefault();
            overlay.setPointerCapture(e.pointerId);
            const rect = overlay.getBoundingClientRect();
            drag = {
                pointerId: e.pointerId,
                rect,
                startX: clamp((e.clientX - rect.left) / rect.width, 0, 1),
                startY: clamp((e.clientY - rect.top) / rect.height, 0, 1),
                currentX: 0,
                currentY: 0,
                pan: e.button === 1 || e.altKey || e.ctrlKey || e.metaKey,
                zoom: {
                    left: parseFloat(overlay.dataset.zoomLeft),
                    top: parseFloat(overlay.dataset.zoomTop),
                    right: parseFloat(overlay.dataset.zoomRight),
                    bottom: parseFloat(overlay.dataset.zoomBottom),
                },
            };
            drag.currentX = drag.startX;
            drag.currentY = drag.startY;
        });

        listen(overlay, "pointermove", e => {
            if (!drag || drag.pointerId !== e.pointerId)
                return;

            const x = clamp((e.clientX - drag.rect.left) / drag.rect.width, 0, 1);
            const y = clamp((e.clientY - drag.rect.top) / drag.rect.height, 0, 1);
            drag.currentX = x;
            drag.currentY = y;

            if (drag.pan) {
                const width = drag.zoom.right - drag.zoom.left;
                const height = drag.zoom.bottom - drag.zoom.top;
                const left = clamp(drag.zoom.left - (x - drag.startX) * width, 0, 1 - width);
                const top = clamp(drag.zoom.top - (y - drag.startY) * height, 0, 1 - height);
                invokeZoom("SetViewport", [left, top, left + width, top + height]);
                return;
            }

            const dx = Math.abs(x - drag.startX) * drag.rect.width;
            const dy = Math.abs(y - drag.startY) * drag.rect.height;
            const horizontal = dx > dy * 2;
            const vertical = dy > dx * 2;
            const left = horizontal || !vertical ? Math.min(drag.startX, x) : 0;
            const right = horizontal || !vertical ? Math.max(drag.startX, x) : 1;
            const top = vertical || !horizontal ? Math.min(drag.startY, y) : 0;
            const bottom = vertical || !horizontal ? Math.max(drag.startY, y) : 1;
            Object.assign(selection.style, {
                display: "block",
                left: `${left * 100}%`, top: `${top * 100}%`,
                width: `${(right - left) * 100}%`, height: `${(bottom - top) * 100}%`,
            });
        });

        const finishDrag = e => {
            if (!drag || drag.pointerId !== e.pointerId)
                return;

            selection.style.display = "none";
            const finished = drag;
            drag = null;

            if (finished.pan)
                return;

            const dx = Math.abs(finished.currentX - finished.startX) * finished.rect.width;
            const dy = Math.abs(finished.currentY - finished.startY) * finished.rect.height;

            if (Math.hypot(dx, dy) < 6)
                return;

            const horizontal = dx > dy * 2;
            const vertical = dy > dx * 2;
            invokeZoom("DragZoom", [
                horizontal || !vertical ? Math.min(finished.startX, finished.currentX) : 0,
                vertical || !horizontal ? Math.min(finished.startY, finished.currentY) : 0,
                horizontal || !vertical ? Math.max(finished.startX, finished.currentX) : 1,
                vertical || !horizontal ? Math.max(finished.startY, finished.currentY) : 1,
            ]);
        };
        listen(overlay, "pointerup", finishDrag);
        listen(overlay, "pointercancel", finishDrag);
    }

    function initNavigator(prefix) {
        const track = document.getElementById(`${prefix}-track_${chartId}`);
        const win = document.getElementById(`${prefix}-window_${chartId}`);
        const handleL = document.getElementById(`${prefix}-handle-left_${chartId}`);
        const handleR = document.getElementById(`${prefix}-handle-right_${chartId}`);

        if (!track || !win || !handleL || !handleR)
            return;

        let drag = null;

        function begin(mode, e) {
            if (e.button !== 0)
                return;

            e.preventDefault();
            e.stopPropagation();
            track.setPointerCapture(e.pointerId);
            drag = {
                mode,
                pointerId: e.pointerId,
                rect: track.getBoundingClientRect(),
                startX: e.clientX,
                startLeft: parseFloat(win.dataset.left),
                startRight: parseFloat(win.dataset.right),
                domainLeft: parseFloat(track.dataset.domainLeft),
                domainRight: parseFloat(track.dataset.domainRight),
            };
        }

        listen(handleL, "pointerdown", e => begin("left", e));
        listen(handleR, "pointerdown", e => begin("right", e));
        listen(win, "pointerdown", e => begin("pan", e));
        listen(track, "pointerdown", e => {
            if (e.target !== track && e.target.tagName !== "CANVAS")
                return;

            const rect = track.getBoundingClientRect();
            const domainLeft = parseFloat(track.dataset.domainLeft);
            const domainRight = parseFloat(track.dataset.domainRight);
            const center = domainLeft + clamp((e.clientX - rect.left) / rect.width, 0, 1) * (domainRight - domainLeft);
            const width = parseFloat(win.dataset.right) - parseFloat(win.dataset.left);
            const left = clamp(center - width / 2, 0, 1 - width);
            invokeZoom("NavigatorZoom", [left, left + width]);
        });
        listen(track, "pointermove", e => {
            if (!drag || drag.pointerId !== e.pointerId)
                return;

            e.preventDefault();
            const domainWidth = drag.domainRight - drag.domainLeft;
            const delta = (e.clientX - drag.startX) / drag.rect.width * domainWidth;
            const minimum = Math.max(1e-6, domainWidth / Math.max(1, drag.rect.width * 4));
            let left;
            let right;

            if (drag.mode === "left") {
                left = clamp(drag.startLeft + delta, 0, drag.startRight - minimum);
                right = drag.startRight;
            } else if (drag.mode === "right") {
                left = drag.startLeft;
                right = clamp(drag.startRight + delta, drag.startLeft + minimum, 1);
            } else {
                const width = drag.startRight - drag.startLeft;
                left = clamp(drag.startLeft + delta, 0, 1 - width);
                right = left + width;
            }

            invokeZoom("NavigatorZoom", [left, right]);
        });
        const end = e => {
            if (drag?.pointerId === e.pointerId)
                drag = null;
        };
        listen(track, "pointerup", end);
        listen(track, "pointercancel", end);
        listen(track, "wheel", e => {
            e.preventDefault();
            const rect = track.getBoundingClientRect();
            const domainLeft = parseFloat(track.dataset.domainLeft);
            const domainRight = parseFloat(track.dataset.domainRight);
            const anchor = domainLeft + clamp((e.clientX - rect.left) / rect.width, 0, 1) * (domainRight - domainLeft);
            const left = parseFloat(win.dataset.left);
            const right = parseFloat(win.dataset.right);
            const factor = e.deltaY < 0 ? 0.8 : 1.25;
            invokeZoom("NavigatorZoom", [
                clamp(anchor - (anchor - left) * factor, 0, 1),
                clamp(anchor + (right - anchor) * factor, 0, 1),
            ]);
        }, { passive: false });
        listen(win, "keydown", e => {
            if (e.key !== "ArrowLeft" && e.key !== "ArrowRight")
                return;

            e.preventDefault();
            const direction = e.key === "ArrowLeft" ? -1 : 1;
            const left = parseFloat(win.dataset.left);
            const right = parseFloat(win.dataset.right);
            const width = right - left;
            const step = width * (e.shiftKey ? 0.1 : 0.02) * direction;

            if (e.altKey) {
                const nextRight = clamp(right + step, left + 1e-6, 1);
                invokeZoom("NavigatorZoom", [left, nextRight]);
            } else {
                const nextLeft = clamp(left + step, 0, 1 - width);
                invokeZoom("NavigatorZoom", [nextLeft, nextLeft + width]);
            }
        });
    }

    initNavigator("navigator");
    initNavigator("navigator-detail");
    nexus.chart.charts[chartId] = { dispose: () => disposers.forEach(dispose => dispose()) };
};

nexus.chart.dispose = function (chartId) {
    nexus.chart.charts[chartId]?.dispose();
    delete nexus.chart.charts[chartId];
};
