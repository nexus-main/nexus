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

test('zoom refreshes the stationary hovered readout after applying the viewport', async () => {
    const environment = createEnvironment();
    environment.listeners.get('overlay_chart:mousemove')({ clientX: 30, clientY: 70 });
    await environment.frames.shift()();
    environment.calls.length = 0;
    environment.listeners.get('overlay_chart:wheel')({ clientX: 30, clientY: 70, deltaY: -1, shiftKey: false });
    await environment.frames.shift()();
    assert.equal(environment.calls[0][0], 'SetViewport');
    await environment.frames.shift()();
    assert.deepEqual(environment.calls[1], ['PointerMoved', 0.3, 0.7]);
});

test('leaving the plot suppresses the readout refresh queued by zoom', async () => {
    const environment = createEnvironment();
    environment.listeners.get('overlay_chart:mousemove')({ clientX: 30, clientY: 70 });
    await environment.frames.shift()();
    environment.calls.length = 0;
    environment.listeners.get('overlay_chart:wheel')({ clientX: 30, clientY: 70, deltaY: -1, shiftKey: false });
    await environment.frames.shift()();
    environment.listeners.get('overlay_chart:mouseleave')();
    await environment.frames.shift()();
    assert.equal(environment.calls.length, 1);
    assert.equal(environment.calls[0][0], 'SetViewport');
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

test('touch double-tap resets the main chart viewport', async () => {
    const environment = createEnvironment();
    const overlay = environment.context.document.getElementById('overlay_chart');
    overlay.dataset.zoomLeft = '0.25';
    overlay.dataset.zoomRight = '0.75';
    overlay.dataset.zoomTop = '0.2';
    overlay.dataset.zoomBottom = '0.8';
    const down = environment.listeners.get('overlay_chart:pointerdown');
    down({ button: 0, pointerId: 1, pointerType: 'touch', clientX: 40, clientY: 60, timeStamp: 100, cancelable: true, preventDefault() {} });
    down({ button: 0, pointerId: 2, pointerType: 'touch', clientX: 45, clientY: 62, timeStamp: 320, cancelable: true, preventDefault() {} });
    await environment.frames.shift()();

    assert.deepEqual(environment.calls, [['SetViewport', 0, 0, 1, 1]]);
    assert.equal(overlay.dataset.zoomLeft, '0');
    assert.equal(overlay.dataset.zoomRight, '1');
    assert.equal(overlay.dataset.zoomTop, '0');
    assert.equal(overlay.dataset.zoomBottom, '1');
});

test('touch taps too far apart do not reset the viewport', () => {
    const environment = createEnvironment();
    const down = environment.listeners.get('overlay_chart:pointerdown');
    down({ button: 0, pointerId: 1, pointerType: 'touch', clientX: 10, clientY: 10, timeStamp: 100, cancelable: true, preventDefault() {} });
    down({ button: 0, pointerId: 2, pointerType: 'touch', clientX: 60, clientY: 60, timeStamp: 250, cancelable: true, preventDefault() {} });

    assert.deepEqual(environment.calls, []);
    assert.equal(environment.frames.length, 0);
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

    const [method, left, top, right, bottom] = environment.calls[0];
    assert.equal(method, 'SetViewport');
    assert.equal(top, 0);
    assert.equal(bottom, 1);
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

    const [method, left, top, right, bottom] = environment.calls[0];
    assert.equal(method, 'SetViewport');
    assert.equal(top, 0);
    assert.equal(bottom, 1);
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

test('main wheel restores time first, then values, including at plot edges', async () => {
    for (const anchor of [0, 50, 100]) {
        const environment = createEnvironment();
        const overlay = environment.context.document.getElementById('overlay_chart');
        Object.assign(overlay.dataset, { zoomLeft: '0.25', zoomRight: '0.75', zoomTop: '0.25', zoomBottom: '0.75' });
        const wheel = environment.listeners.get('overlay_chart:wheel');
        for (let index = 0; index < 20; index++) {
            wheel({ clientX: anchor, clientY: anchor, deltaY: 1, shiftKey: false });
            await environment.frames.shift()();
            const [method, left, top, right, bottom] = environment.calls.at(-1);
            assert.equal(method, 'SetViewport');
            if (left > 0 || right < 1) {
                assert.equal(top, 0.25);
                assert.equal(bottom, 0.75);
            } else {
                assert.ok(bottom - top > 0.5);
            }
        }
        assert.deepEqual(environment.calls.at(-1), ['SetViewport', 0, 0, 1, 1]);
    }
});

test('coalesced wheel events retain vertical expansion when time reaches full width', async () => {
    const environment = createEnvironment();
    const overlay = environment.context.document.getElementById('overlay_chart');
    Object.assign(overlay.dataset, { zoomLeft: '0.02', zoomRight: '0.98', zoomTop: '0.25', zoomBottom: '0.75' });
    const wheel = environment.listeners.get('overlay_chart:wheel');
    wheel({ clientX: 50, clientY: 50, deltaY: 1, shiftKey: false });
    const firstHeight = Number(overlay.dataset.zoomBottom) - Number(overlay.dataset.zoomTop);
    assert.ok(firstHeight > 0.5);
    wheel({ clientX: 50, clientY: 50, deltaY: 1, shiftKey: false });
    const top = Number(overlay.dataset.zoomTop);
    const bottom = Number(overlay.dataset.zoomBottom);
    assert.ok(bottom - top > firstHeight);
    wheel({ clientX: 50, clientY: 50, deltaY: -1, shiftKey: false });
    assert.equal(environment.frames.length, 1);
    await environment.frames.shift()();
    assert.deepEqual(environment.calls, [['SetViewport', Number(overlay.dataset.zoomLeft), top, Number(overlay.dataset.zoomRight), bottom]]);
    assert.ok(Number(overlay.dataset.zoomLeft) > 0);
});

test('Shift-wheel keeps the existing vertical-only callback', async () => {
    const environment = createEnvironment();
    const wheel = environment.listeners.get('overlay_chart:wheel');
    for (const deltaY of [-1, 1]) {
        wheel({ clientX: 30, clientY: 70, deltaY, shiftKey: true });
        await environment.frames.shift()();
        assert.deepEqual(environment.calls.at(-1), ['WheelZoom', 0.3, 0.7, deltaY, true]);
    }
});
