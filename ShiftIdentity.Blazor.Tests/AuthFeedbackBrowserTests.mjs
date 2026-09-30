import { test } from 'node:test';
import assert from 'node:assert/strict';

// A DOM-free test of the step error panel's closing wait. The fakes implement only what auth-feedback.js calls.
async function withBrowser(run, { frames = true, document = undefined } = {}) {
    const names = ['requestAnimationFrame', 'document'];
    const original = Object.fromEntries(names.map(name => [name, globalThis[name]]));
    // A hidden page paints no frames.
    globalThis.requestAnimationFrame = callback => frames ? setImmediate(callback) : 0;
    globalThis.document = document;
    try { await run(await import('../ShiftIdentity.Dashboard.Blazor/wwwroot/auth-feedback.js')); }
    finally { Object.assign(globalThis, original); }
}

const slot = animations => ({ getAnimations: () => animations });
const transition = milliseconds => ({
    finished: new Promise(resolve => setTimeout(resolve, milliseconds)),
    effect: { getTiming: () => ({ iterations: 1 }) }
});
const elapsed = async wait => { const started = performance.now(); await wait; return performance.now() - started; };

test('without a closing transition (reduced motion) the panel still stays away for the minimum absence', async () => {
    await withBrowser(async feedback => {
        assert.ok(await elapsed(feedback.waitForClose(slot([]))) >= feedback.minimumAbsence - 5);
    });
});

test('a closing transition is waited for to its end, and a ripple that never ends is not', async () => {
    await withBrowser(async feedback => {
        const ripple = { finished: new Promise(() => {}), effect: { getTiming: () => ({ iterations: Infinity }) } };
        assert.ok(await elapsed(feedback.waitForClose(slot([transition(400), ripple]))) >= 395);
    });
});

test('a hidden page that paints no frames does not hold the attempt back', async () => {
    await withBrowser(async feedback => {
        const time = await elapsed(feedback.waitForClose(slot([])));
        assert.ok(time >= feedback.minimumAbsence - 5 && time < 1000, `${time} ms`);
    }, { frames: false });
});

test('after a press that the form refused, the first field that says what to fix gets the focus', async () => {
    const focused = [];
    const field = { focus: () => focused.push('code') };
    const form = { querySelector: selector => selector === '[aria-invalid="true"]' ? field : null };
    await withBrowser(async feedback => {
        await feedback.focusInvalid('auth-form-1');
        await feedback.focusInvalid('a-form-that-has-gone');
        assert.deepEqual(focused, ['code']);
    }, { document: { getElementById: id => id === 'auth-form-1' ? form : null } });
});
