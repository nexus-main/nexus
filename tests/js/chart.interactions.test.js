const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

function createEnvironment() {
    const listeners = new Map();
    const frames = [];
    const calls = [];
    const elements = new Map();
    const element = id => ({
        id,
        dataset: {
            zoomLeft: '0', zoomTop: '0', zoomRight: '1', zoomBottom: '1',
            minimumHorizontalZoom: '2e-16',
        },
        style: { removeProperty() {} },
        addEventListener(name, handler) { listeners.set(`${id}:${name}`, handler); },
        removeEventListener(name) { listeners.delete(`${id}:${name}`); },
        getBoundingClientRect() { return { left: 0, top: 0, width: 100, height: 100 }; },
        setPointerCapture() {},
    });
    for (const id of [
        'overlay_chart', 'selection_chart',
        'navigator-track_chart', 'navigator-window_chart',
        'navigator-handle-left_chart', 'navigator-handle-right_chart',
    ])
        elements.set(id, element(id));
    elements.get('navigator-track_chart').dataset = { domainLeft: '0', domainRight: '1' };
    elements.get('navigator-window_chart').dataset = { left: '0', right: '0.1' };

    const context = {
        nexus: {},
        console: { error() {} },
        Math,
        Number,
        document: { getElementById(id) { return elements.get(id) ?? null; } },
        requestAnimationFrame(callback) { frames.push(callback); },
    };
    context.window = context;
    context.globalThis = context;
    vm.runInNewContext(
        fs.readFileSync(path.join(__dirname, '../../src/Nexus/wwwroot/js/chart.js'), 'utf8'),
        context,
        { filename: 'chart.js' });

    const helper = {
        invokeMethodAsync(method, ...values) {
            calls.push([method, ...values]);
            return Promise.resolve();
        },
    };
    context.nexus.chart.initInteractions('chart', helper);
    return { context, listeners, frames, calls };
}

test('pointer movement is coalesced to the latest position per frame', async () => {
    const environment = createEnvironment();
    const move = environment.listeners.get('overlay_chart:mousemove');
    move({ clientX: 10, clientY: 20 });
    move({ clientX: 80, clientY: 90 });

    assert.equal(environment.frames.length, 1);
    await environment.frames.shift()();
    assert.deepEqual(environment.calls, [['PointerMoved', 0.8, 0.9]]);
});

test('disposing interactions removes pointer listeners and suppresses queued callbacks', async () => {
    const environment = createEnvironment();
    environment.listeners.get('overlay_chart:mousemove')({ clientX: 50, clientY: 50 });
    environment.context.nexus.chart.dispose('chart');
    await environment.frames.shift()();

    assert.equal(environment.listeners.has('overlay_chart:mousemove'), false);
    assert.deepEqual(environment.calls, []);
});

test('pointer leave clears auxiliary state without calling .NET', () => {
    const environment = createEnvironment();
    environment.listeners.get('overlay_chart:mouseleave')();

    assert.deepEqual(environment.calls, []);
});

test('disposing interactions suppresses queued zoom callbacks', async () => {
    const environment = createEnvironment();
    environment.listeners.get('overlay_chart:wheel')({
        clientX: 50,
        clientY: 50,
        deltaY: -1,
        shiftKey: false,
        cancelable: true,
        preventDefault() {},
    });
    environment.context.nexus.chart.dispose('chart');
    await environment.frames.shift()();

    assert.deepEqual(environment.calls, []);
});

test('navigator zoom-out preserves expansion at a domain edge', async () => {
    const environment = createEnvironment();
    environment.listeners.get('navigator-track_chart:wheel')({
        clientX: 10,
        deltaY: 1,
        cancelable: true,
        preventDefault() {},
    });
    await environment.frames.shift()();

    const [method, left, right] = environment.calls[0];
    assert.equal(method, 'NavigatorZoom');
    assert.equal(left, 0);
    assert.ok(right > 0.1);
});

test('main wheel zooms out from a collapsed billion-point viewport', async () => {
    const environment = createEnvironment();
    const overlay = environment.context.document.getElementById('overlay_chart');
    overlay.dataset.zoomLeft = '1';
    overlay.dataset.zoomRight = '1';
    environment.listeners.get('overlay_chart:wheel')({
        clientX: 100,
        clientY: 50,
        deltaY: 1,
        shiftKey: false,
        cancelable: true,
        preventDefault() {},
    });
    await environment.frames.shift()();

    const [method, left, right] = environment.calls[0];
    assert.equal(method, 'NavigatorZoom');
    assert.ok(left < right);
    assert.ok(right - left > 2e-16);
    assert.equal(right, 1);
});

test('main wheel sends finite values when viewport data is missing', async () => {
    const environment = createEnvironment();
    const overlay = environment.context.document.getElementById('overlay_chart');
    delete overlay.dataset.zoomLeft;
    delete overlay.dataset.zoomRight;
    delete overlay.dataset.minimumHorizontalZoom;
    environment.listeners.get('overlay_chart:wheel')({
        clientX: 50,
        clientY: 50,
        deltaY: -1,
        shiftKey: false,
        cancelable: true,
        preventDefault() {},
    });
    await environment.frames.shift()();

    const [method, left, right] = environment.calls[0];
    assert.equal(method, 'NavigatorZoom');
    assert.equal(Number.isFinite(left), true);
    assert.equal(Number.isFinite(right), true);
    assert.ok(left < right);
});

test('main wheel zooms out after repeatedly reaching the billion-point limit', async () => {
    const environment = createEnvironment();
    const wheel = environment.listeners.get('overlay_chart:wheel');
    const event = deltaY => ({
        clientX: 50,
        clientY: 50,
        deltaY,
        shiftKey: false,
        cancelable: true,
        preventDefault() {},
    });

    for (let index = 0; index < 260; index++) {
        wheel(event(-1));
        await environment.frames.shift()();
    }

    const overlay = environment.context.document.getElementById('overlay_chart');
    const minimumWidth = parseFloat(overlay.dataset.minimumHorizontalZoom);
    const widthAtLimit = parseFloat(overlay.dataset.zoomRight) - parseFloat(overlay.dataset.zoomLeft);
    assert.ok(widthAtLimit >= minimumWidth);
    assert.ok(widthAtLimit < minimumWidth * 2);

    wheel(event(1));
    await environment.frames.shift()();
    const widthAfterZoomOut = parseFloat(overlay.dataset.zoomRight) - parseFloat(overlay.dataset.zoomLeft);
    assert.ok(widthAfterZoomOut > widthAtLimit);
});
