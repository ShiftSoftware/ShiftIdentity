import { test } from 'node:test';
import assert from 'node:assert/strict';
import { watch } from '../ShiftIdentity.Dashboard.Blazor/wwwroot/development-codes.js';

test('expiry, tab resume and disposal hide codes and clean up listeners', async () => {
    // A DOM-free test of the visibility bridge; real rollover is also exercised in both browsers surfaces.
    const original = { document: globalThis.document, window: globalThis.window,
        setTimeout: globalThis.setTimeout, clearTimeout: globalThis.clearTimeout };
    const document = new EventTarget();
    document.hidden = false;
    const window = new EventTarget();
    const timers = new Map(); let nextTimer = 0;
    globalThis.document = document; globalThis.window = window;
    globalThis.setTimeout = callback => { timers.set(++nextTimer, callback); return nextTimer; };
    globalThis.clearTimeout = id => timers.delete(id);
    try {
        let suspended = false;
        const calls = [];
        const element = { setAttribute: () => suspended = true, removeAttribute: () => suspended = false };
        const receiver = { invokeMethodAsync: async (...args) => calls.push(args) };
        const watcher = watch(element, receiver);
        watcher.arm(5000); assert.equal(suspended, false); assert.equal(timers.size, 1);
        [...timers.values()][0](); assert.equal(suspended, true);
        watcher.arm(0); assert.equal(suspended, true); assert.equal(timers.size, 0);
        document.hidden = true; document.dispatchEvent(new Event('visibilitychange'));
        watcher.arm(5000); assert.equal(suspended, true); assert.equal(timers.size, 0);
        document.hidden = false; document.dispatchEvent(new Event('visibilitychange'));
        assert.equal(suspended, true); // .NET must fetch before arming a new display deadline.
        assert.deepEqual(calls, [['VisibilityChanged', false], ['VisibilityChanged', true]]);
        watcher.arm(5000); assert.equal(suspended, false);
        window.dispatchEvent(new Event('pageshow')); assert.equal(suspended, true);
        assert.equal(calls.length, 3);
        watcher.dispose(); assert.equal(timers.size, 0);
        document.dispatchEvent(new Event('visibilitychange')); window.dispatchEvent(new Event('pageshow'));
        assert.equal(calls.length, 3);
        watcher.arm(5000); assert.equal(suspended, true);
    } finally { Object.assign(globalThis, original); }
});
