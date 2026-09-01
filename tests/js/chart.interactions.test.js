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
        dataset: { zoomLeft: '0', zoomTop: '0', zoomRight: '1', zoomBottom: '1' },
        style: { removeProperty() {} },
        addEventListener(name, handler) { listeners.set(`${id}:${name}`, handler); },
        removeEventListener(name) { listeners.delete(`${id}:${name}`); },
        getBoundingClientRect() { return { left: 0, top: 0, width: 100, height: 100 }; },
        setPointerCapture() {},
    });
    for (const id of ['overlay_chart', 'selection_chart'])
        elements.set(id, element(id));

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
