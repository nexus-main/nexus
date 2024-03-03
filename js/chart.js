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