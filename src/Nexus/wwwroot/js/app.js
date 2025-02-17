nexus = {};
nexus.util = {};

/* sometimes Console.WriteLine does not work */
nexus.util.log = function (message) {
    console.log(message)
}

nexus.util.highlight = function (code, language) {
    return hljs.highlight(code, { language: language }).value
}

nexus.util.blobSaveAs = function (filename, bytesBase64) {

    let link = document.createElement('a');
    link.download = filename;
    link.href = "data:application/octet-stream;base64," + bytesBase64;

    document.body.appendChild(link); // Needed for Firefox
    link.click();
    document.body.removeChild(link);
}

nexus.util.addMouseUpEvent = function (dotNetHelper) {

    window.addEventListener("mouseup", e => dotNetHelper.invokeMethodAsync("OnMouseUp"), {
        once: true
    });
}

nexus.util.addClickEvent = function (dotNetHelper) {

    window.addEventListener("click", e => dotNetHelper.invokeMethodAsync("OnClick"), {
        once: true
    });
}

nexus.util.saveSetting = function (key, value) {
    if (window.localStorage)
        localStorage.setItem(key, JSON.stringify(value));
}

nexus.util.clearSetting = function (key, value) {
    if (window.localStorage)
        localStorage.removeItem(key);
}

nexus.util.loadSetting = function (key) {

    if (window.localStorage)
        return JSON.parse(localStorage.getItem(key));

    else
        return null
}

nexus.util.copyToClipboard = function (text) {
    navigator.clipboard.writeText(text)
}