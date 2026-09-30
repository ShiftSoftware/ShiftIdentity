import { test } from 'node:test';
import assert from 'node:assert/strict';

// A DOM-free test of the login box's view transition. The fake elements implement only what login-box.js calls.
function fakeElement(name) {
    const element = {
        name, children: [], parent: null, inert: false, attributes: {}, offsetHeight: 100, style: {}, isConnected: true,
        classList: { add() {}, remove() {} }, dataset: {},
        setAttribute(key, value) { this.attributes[key] = value; },
        querySelectorAll() { return []; },
        querySelector() { return null; },
        closest() { return null; },
        getAnimations() { return []; },
        animate() {
            return { finished: Promise.resolve(), cancel() {}, playState: 'finished', effect: { getTiming: () => ({ iterations: 1 }) } };
        },
        cloneNode() { return fakeElement(`copy of ${this.name}`); },
        append(child) { child.parent = this; this.children.push(child); },
        replaceChildren() { this.children.forEach(child => child.parent = null); this.children = []; },
        remove() {
            if (!this.parent) return;
            this.parent.children = this.parent.children.filter(child => child !== this);
            this.parent = null;
        },
        get firstElementChild() { return this.children[0] ?? null; }
    };
    return element;
}

function fakeBox() {
    const viewport = fakeElement('viewport'), content = fakeElement('content'), outgoing = fakeElement('outgoing');
    const root = fakeElement('root');
    root.querySelector = selector => ({
        '.identity-login-viewport': viewport, '.identity-login-content': content, '.identity-login-outgoing': outgoing
    })[selector] ?? null;
    return { root, content, outgoing };
}

async function withBrowser(run) {
    const names = ['ResizeObserver', 'matchMedia', 'requestAnimationFrame', 'getComputedStyle', 'history'];
    const original = Object.fromEntries(names.map(name => [name, globalThis[name]]));
    globalThis.ResizeObserver = class { observe() {} disconnect() {} };
    globalThis.matchMedia = () => ({ matches: false });
    globalThis.requestAnimationFrame = callback => setImmediate(callback);
    globalThis.getComputedStyle = () => ({ direction: 'ltr' });
    globalThis.history = { state: null, replaceState() {}, back() {} };
    try { await run(await import('../ShiftIdentity.Dashboard.Blazor/wwwroot/login-box.js')); }
    finally { Object.assign(globalThis, original); }
}

test('a view change animates the copy of the previous view out and removes it', async () => {
    await withBrowser(async box => {
        const { root, content, outgoing } = fakeBox();
        await box.sync(root, 'login', 1, false);
        content.name = 'login form';
        box.prepare(root);
        assert.equal(outgoing.children.length, 1);
        assert.equal(outgoing.children[0].inert, true);
        content.name = 'recovery form';
        await box.sync(root, 'recovery', 1, false);
        assert.equal(outgoing.children.length, 0);
        box.dispose(root);
    });
});

test('a copy that no view change claims is removed at once, so it cannot stay behind the live form', async () => {
    await withBrowser(async box => {
        const { root, content, outgoing } = fakeBox();
        await box.sync(root, 'login', 1, false);
        box.prepare(root);
        content.name = 'recovery form';
        await box.sync(root, 'recovery', 1, false);
        // The reported fault: a second copy taken after the new view was already on the page. Its sync sees no
        // change of view, so no transition would ever animate it out.
        box.prepare(root);
        assert.equal(outgoing.children[0].name, 'copy of recovery form');
        const settling = box.sync(root, 'recovery', 1, false);
        assert.equal(outgoing.children.length, 0, 'removed before the sync waits for anything');
        await settling;
        assert.equal(outgoing.children.length, 0);
        box.dispose(root);
    });
});

test('a step error that opens within a view lets the box follow it, and a view change keeps the height transition', async () => {
    await withBrowser(async box => {
        const { root, content } = fakeBox();
        const classes = [];
        root.classList = { add: name => classes.push(`+${name}`), remove: name => classes.push(`-${name}`) };
        const slot = fakeElement('error slot');
        slot.dataset.open = 'false';
        content.querySelectorAll = selector => selector === '.auth-feedback-slot' ? [slot] : [];
        await box.sync(root, 'password', 1, false);
        classes.length = 0;

        slot.dataset.open = 'true';
        await box.sync(root, 'password', 1, false);
        assert.deepEqual(classes, ['+feedback-resizing', '-feedback-resizing']);

        classes.length = 0;
        await box.sync(root, 'password', 1, false);
        assert.deepEqual(classes, ['-feedback-resizing'], 'nothing opened or closed');

        classes.length = 0;
        slot.dataset.open = 'false';
        await box.sync(root, 'code', 1, false);
        assert.deepEqual(classes.filter(name => name.startsWith('+')), [], 'the new view animates the box height as before');
        box.dispose(root);
    });
});

test('the sign-in failure panel stays away for the minimum absence before a retry, as every step error does', async () => {
    await withBrowser(async box => {
        const { minimumAbsence } = await import('../ShiftIdentity.Dashboard.Blazor/wwwroot/auth-feedback.js');
        const { root } = fakeBox();
        await box.sync(root, 'login', 1, false);
        const started = performance.now();
        await box.waitForFeedbackExit(root);
        assert.ok(performance.now() - started >= minimumAbsence - 5);
        box.dispose(root);
    });
});
