'use strict';
// Tests of the issuer-neutral core with a fake store, a fake issuer and a fake clock. No network, no browser.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const TokenClient = require('../src/shiftidentity-tokenclient.js');

const SOURCE = path.join(__dirname, '..', 'src');
const T0 = Date.UTC(2026, 9, 8, 10, 0, 0);

function copy(value) {
    return value === null || value === undefined ? null : JSON.parse(JSON.stringify(value));
}

// An asynchronous in-memory store. A read sees the value at call time; writes can be held to test ordering.
function memoryStore(initial) {
    const store = {
        value: copy(initial),
        reads: 0, writes: 0, clears: 0,
        readError: null, writeError: null, clearError: null,
        holdWrites: false, heldWrites: [],
        read(callback) {
            store.reads++;
            const value = copy(store.value), error = store.readError;
            setImmediate(() => callback(error, error ? undefined : value));
        },
        write(session, callback) {
            const apply = () => {
                if (store.writeError) return callback(store.writeError);
                store.value = copy(session);
                store.writes++;
                callback(null);
            };
            if (store.holdWrites) store.heldWrites.push(apply);
            else setImmediate(apply);
        },
        clear(callback) {
            const error = store.clearError;
            setImmediate(() => {
                if (!error) { store.value = null; store.clears++; }
                callback(error);
            });
        }
    };
    return store;
}

// An issuer whose refreshes the test answers by hand.
function controlledIssuer() {
    const issuer = {
        id: 'test-issuer',
        contractVersion: 1,
        calls: [],
        refresh(session, transport, callback) { issuer.calls.push({ session, transport, callback }); },
        answer(result, index = issuer.calls.length - 1) { issuer.calls[index].callback(result); }
    };
    return issuer;
}

function session(clock, { access = 300, refresh = 3600, n = 1, refreshToken } = {}) {
    return {
        accessToken: 'access-' + n,
        accessExpiresAt: clock.t + access * 1000,
        refreshToken: refreshToken === undefined ? 'refresh-' + n : refreshToken,
        refreshExpiresAt: refresh === null ? null : clock.t + refresh * 1000,
        extra: { userData: { username: 'user-' + n } }
    };
}

function setup({ stored, options = {} } = {}) {
    const clock = { t: T0 };
    clock.now = () => clock.t;
    const store = memoryStore(stored ? stored(clock) : null);
    const issuer = controlledIssuer();
    const client = new TokenClient(Object.assign({ store, issuer, now: clock.now }, options));
    return { clock, store, issuer, client };
}

async function waitFor(condition, message) {
    for (let i = 0; i < 200; i++) {
        if (condition()) return;
        await new Promise(resolve => setImmediate(resolve));
    }
    assert.fail(message || 'The condition did not become true.');
}

async function ticks(count = 10) {
    for (let i = 0; i < count; i++) await new Promise(resolve => setTimeout(resolve, 0));
}

test('hands out the stored access token while more than the skew is left, without a refresh', async () => {
    const { client, issuer } = setup({ stored: clock => session(clock, { access: 61 }) });
    const outcome = await client.acquireToken();
    assert.deepEqual(outcome, {
        kind: 'tokenAvailable', token: 'access-1', expiresAt: T0 + 61000, extra: { userData: { username: 'user-1' } }
    });
    assert.equal(issuer.calls.length, 0);
});

test('skew boundary: exactly the skew left needs no refresh, one millisecond less does', async () => {
    const { client, issuer, clock } = setup({ stored: clock => session(clock, { access: 300 }) });

    clock.t = T0 + 240000; // 60 000 ms left
    assert.equal((await client.acquireToken()).token, 'access-1');
    assert.equal(issuer.calls.length, 0);

    clock.t = T0 + 240001; // 59 999 ms left
    const pending = client.acquireToken();
    await waitFor(() => issuer.calls.length === 1, 'The client did not refresh inside the skew.');
    assert.equal(issuer.calls[0].session.refreshToken, 'refresh-1');
    issuer.answer({ kind: 'session', session: session(clock, { access: 900, n: 2 }) });
    assert.equal((await pending).token, 'access-2');
});

test('skewSeconds moves the refresh boundary', async () => {
    const { client, issuer, clock } = setup({ stored: clock => session(clock, { access: 300 }), options: { skewSeconds: 10 } });
    clock.t = T0 + 290000; // exactly 10 s left
    assert.equal((await client.acquireToken()).token, 'access-1');
    clock.t += 1;
    const pending = client.acquireToken();
    await waitFor(() => issuer.calls.length === 1);
    issuer.answer({ kind: 'session', session: session(clock, { n: 2 }) });
    assert.equal((await pending).token, 'access-2');
});

test('concurrent callers share one refresh and one store write', async () => {
    const { client, issuer, store, clock } = setup({ stored: clock => session(clock, { access: 0 }) });
    const callers = [client.acquireToken(), client.acquireToken(), client.acquireToken()];
    await waitFor(() => issuer.calls.length === 1);
    // A caller that arrives while the refresh runs joins it.
    callers.push(client.acquireToken());
    await ticks();
    assert.equal(issuer.calls.length, 1);
    issuer.answer({ kind: 'session', session: session(clock, { access: 900, n: 2 }) });
    const outcomes = await Promise.all(callers);
    assert.deepEqual(outcomes.map(outcome => outcome.token), ['access-2', 'access-2', 'access-2', 'access-2']);
    assert.equal(store.writes, 1);
    assert.equal(store.reads, 1);

    // The next call reads the new session and needs no refresh.
    assert.equal((await client.acquireToken()).token, 'access-2');
    assert.equal(issuer.calls.length, 1);
});

test('the token is reported only after the new session is stored', async () => {
    const { client, issuer, store, clock } = setup({ stored: clock => session(clock, { access: 0 }) });
    store.holdWrites = true;
    let outcome = null;
    const pending = client.acquireToken().then(value => { outcome = value; });
    await waitFor(() => issuer.calls.length === 1);
    issuer.answer({ kind: 'session', session: session(clock, { n: 2 }) });
    await waitFor(() => store.heldWrites.length === 1);
    await ticks();
    assert.equal(outcome, null, 'The outcome arrived before the store write finished.');
    store.heldWrites[0]();
    await pending;
    assert.equal(outcome.token, 'access-2');
    assert.equal(store.value.refreshToken, 'refresh-2');
});

test('a rejected refresh clears the store and returns signInRequired with the reason and step', async () => {
    const { client, issuer, store } = setup({ stored: clock => session(clock, { access: 30 }) });
    const pending = client.acquireToken();
    await waitFor(() => issuer.calls.length === 1);
    issuer.answer({ kind: 'rejected', reason: 'challenge', step: 'PasswordChange' });
    assert.deepEqual(await pending, { kind: 'signInRequired', reason: 'challenge', step: 'PasswordChange' });
    assert.equal(store.value, null);
    assert.equal(store.clears, 1);

    // The session is gone, so the issuer is not asked again.
    assert.deepEqual(await client.acquireToken(), { kind: 'signInRequired', reason: 'noSession' });
    assert.equal(issuer.calls.length, 1);
});

test('a rejection without a reason still returns signInRequired', async () => {
    const { client, issuer } = setup({ stored: clock => session(clock, { access: 0 }) });
    const pending = client.acquireToken();
    await waitFor(() => issuer.calls.length === 1);
    issuer.answer({ kind: 'rejected' });
    assert.deepEqual(await pending, { kind: 'signInRequired', reason: 'rejected' });
});

test('an unavailable refresh keeps the store and returns temporarilyUnavailable when the access token has expired', async () => {
    const { client, issuer, store } = setup({ stored: clock => session(clock, { access: 0 }) });
    const before = copy(store.value);
    const pending = client.acquireToken();
    await waitFor(() => issuer.calls.length === 1);
    issuer.answer({ kind: 'unavailable' });
    assert.deepEqual(await pending, { kind: 'temporarilyUnavailable', retryAfter: 2 });
    assert.deepEqual(store.value, before);
    assert.equal(store.clears, 0);
});

test('an unavailable refresh still hands out the current access token while it has not expired', async () => {
    const { client, issuer, store } = setup({ stored: clock => session(clock, { access: 30 }) });
    const pending = client.acquireToken();
    await waitFor(() => issuer.calls.length === 1);
    issuer.answer({ kind: 'unavailable' });
    const outcome = await pending;
    assert.equal(outcome.kind, 'tokenAvailable');
    assert.equal(outcome.token, 'access-1');
    assert.equal(store.writes, 0);
});

test('back-off: no issuer call during the delay, the delay doubles up to the maximum, and success resets it', async () => {
    const { client, issuer, clock } = setup({ stored: clock => session(clock, { access: 0, refresh: 100000 }) });
    async function attempt(result) {
        const calls = issuer.calls.length;
        const pending = client.acquireToken();
        await waitFor(() => issuer.calls.length === calls + 1, 'The client did not call the issuer after the delay.');
        issuer.answer(result);
        return pending;
    }

    const delays = [];
    for (let i = 0; i < 7; i++) {
        const outcome = await attempt({ kind: 'unavailable' });
        assert.equal(outcome.kind, 'temporarilyUnavailable');
        delays.push(outcome.retryAfter);

        // Inside the delay, the issuer is not called and the remaining time is reported.
        clock.t += outcome.retryAfter * 1000 - 1000;
        const waiting = await client.acquireToken();
        assert.deepEqual(waiting, { kind: 'temporarilyUnavailable', retryAfter: 1 });
        clock.t += 1000;
    }
    assert.deepEqual(delays, [2, 4, 8, 16, 32, 60, 60]);
    assert.equal(issuer.calls.length, 7);

    // A longer delay asked for by the issuer is honoured.
    assert.equal((await attempt({ kind: 'unavailable', retryAfter: 120 })).retryAfter, 120);
    clock.t += 120000;

    // Success resets the delay.
    const refreshed = await attempt({ kind: 'session', session: session(clock, { access: 0, refresh: 100000, n: 2 }) });
    assert.equal(refreshed.kind, 'tokenAvailable');
    assert.equal((await attempt({ kind: 'unavailable' })).retryAfter, 2);
});

test('backoffInitialSeconds and backoffMaxSeconds set the delays', async () => {
    const { client, issuer, clock } = setup({
        stored: clock => session(clock, { access: 0 }), options: { backoffInitialSeconds: 5, backoffMaxSeconds: 12 }
    });
    const delays = [];
    for (let i = 0; i < 3; i++) {
        const pending = client.acquireToken();
        await waitFor(() => issuer.calls.length === i + 1);
        issuer.answer({ kind: 'unavailable' });
        const outcome = await pending;
        delays.push(outcome.retryAfter);
        clock.t += outcome.retryAfter * 1000;
    }
    assert.deepEqual(delays, [5, 10, 12]);
});

test('no stored session returns signInRequired without calling the issuer', async () => {
    const { client, issuer } = setup();
    assert.deepEqual(await client.acquireToken(), { kind: 'signInRequired', reason: 'noSession' });
    assert.equal(issuer.calls.length, 0);
});

test('a stored value without tokens counts as no session', async () => {
    const { client, issuer } = setup({ stored: () => ({ accessToken: '', refreshToken: 7 }) });
    assert.deepEqual(await client.acquireToken(), { kind: 'signInRequired', reason: 'noSession' });
    assert.equal(issuer.calls.length, 0);
});

test('an expired refresh token returns signInRequired without calling the issuer, and the store is kept', async () => {
    const { client, issuer, store, clock } = setup({ stored: clock => session(clock, { access: 300, refresh: 600 }) });
    clock.t = T0 + 600000;
    assert.deepEqual(await client.acquireToken(), { kind: 'signInRequired', reason: 'refreshTokenExpired' });
    assert.equal(issuer.calls.length, 0);
    assert.notEqual(store.value, null);
});

test('without a usable refresh token, the access token is handed out until it expires', async () => {
    const { client, issuer, clock } = setup({ stored: clock => session(clock, { access: 30, refreshToken: '' }) });
    assert.equal((await client.acquireToken()).token, 'access-1');
    clock.t = T0 + 30000;
    assert.deepEqual(await client.acquireToken(), { kind: 'signInRequired', reason: 'noRefreshToken' });
    assert.equal(issuer.calls.length, 0);
});

test('an unknown refresh expiry is left to the issuer', async () => {
    const { client, issuer, clock } = setup({ stored: clock => session(clock, { access: 0, refresh: null }) });
    const pending = client.acquireToken();
    await waitFor(() => issuer.calls.length === 1);
    issuer.answer({ kind: 'session', session: session(clock, { n: 2 }) });
    assert.equal((await pending).token, 'access-2');
});

test('clear during a refresh: waiters get signInRequired, the late session is discarded, and the next call starts fresh', async () => {
    const { client, issuer, store, clock } = setup({ stored: clock => session(clock, { access: 0 }) });
    const first = client.acquireToken();
    const second = client.acquireToken();
    await waitFor(() => issuer.calls.length === 1);

    await client.clear();
    assert.deepEqual(await first, { kind: 'signInRequired', reason: 'cleared' });
    assert.deepEqual(await second, { kind: 'signInRequired', reason: 'cleared' });

    // A new sign-in writes a new session. The next call does not join the old refresh.
    store.value = session(clock, { access: 0, n: 3 });
    const next = client.acquireToken();
    await waitFor(() => issuer.calls.length === 2, 'The call after clear joined the old refresh.');
    assert.equal(issuer.calls[1].session.refreshToken, 'refresh-3');

    // The old refresh ends late. Its session must not reach the store or any caller.
    issuer.answer({ kind: 'session', session: session(clock, { n: 2 }) }, 0);
    await ticks();
    assert.equal(store.writes, 0);
    assert.equal(store.value.refreshToken, 'refresh-3');

    issuer.answer({ kind: 'session', session: session(clock, { n: 4 }) }, 1);
    assert.equal((await next).token, 'access-4');
    assert.equal(store.value.refreshToken, 'refresh-4');
});

test('a late rejection or failure after clear does not touch the store or the back-off', async () => {
    for (const late of [{ kind: 'unavailable', retryAfter: 600 }, { kind: 'rejected', reason: 'InvalidGrant' }]) {
        const { client, issuer, store, clock } = setup({ stored: clock => session(clock, { access: 0 }) });
        const first = client.acquireToken();
        await waitFor(() => issuer.calls.length === 1);
        await client.clear();
        assert.equal((await first).reason, 'cleared');
        store.value = session(clock, { access: 0, n: 2 });

        issuer.answer(late, 0);
        await ticks();
        assert.equal(store.clears, 1, late.kind + ' cleared the new session.');
        assert.equal(store.value.refreshToken, 'refresh-2');

        // No back-off was recorded, so the next call asks the issuer at once.
        const next = client.acquireToken();
        await waitFor(() => issuer.calls.length === 2, late.kind + ' recorded a back-off.');
        issuer.answer({ kind: 'session', session: session(clock, { n: 3 }) }, 1);
        assert.equal((await next).token, 'access-3');
    }
});

test('clear during the store read discards that read', async () => {
    const { client, issuer, store } = setup({ stored: clock => session(clock, { access: 0 }) });
    const pending = client.acquireToken();
    const cleared = client.clear();
    assert.deepEqual(await pending, { kind: 'signInRequired', reason: 'cleared' });
    await cleared;
    await ticks();
    assert.equal(issuer.calls.length, 0, 'The read that started before clear went on to refresh.');
    assert.equal(store.value, null);
});

test('clear resets the back-off', async () => {
    const { client, issuer, store, clock } = setup({ stored: clock => session(clock, { access: 0 }) });
    const pending = client.acquireToken();
    await waitFor(() => issuer.calls.length === 1);
    issuer.answer({ kind: 'unavailable' });
    assert.equal((await pending).kind, 'temporarilyUnavailable');
    await client.clear();
    store.value = session(clock, { access: 0, n: 2 });
    const next = client.acquireToken();
    await waitFor(() => issuer.calls.length === 2, 'The back-off survived clear.');
    issuer.answer({ kind: 'session', session: session(clock, { n: 3 }) });
    assert.equal((await next).token, 'access-3');
});

test('a store write failure rejects acquireToken', async () => {
    const { client, issuer, store, clock } = setup({ stored: clock => session(clock, { access: 0 }) });
    const quota = new Error('QuotaExceededError');
    store.writeError = quota;
    const pending = client.acquireToken();
    await waitFor(() => issuer.calls.length === 1);
    issuer.answer({ kind: 'session', session: session(clock, { n: 2 }) });
    await assert.rejects(pending, error => {
        assert.match(error.message, /failed to write the session/);
        assert.equal(error.cause, quota);
        return true;
    });
});

test('a store read failure rejects acquireToken', async () => {
    const { client, store } = setup();
    store.readError = new Error('SecurityError');
    await assert.rejects(client.acquireToken(), /failed to read the session/);
});

test('a store that throws rejects acquireToken', async () => {
    const { client, store } = setup();
    store.read = () => { throw new Error('storage disabled'); };
    await assert.rejects(client.acquireToken(), error => error.cause.message === 'storage disabled');
});

test('a store clear failure after a rejection rejects acquireToken', async () => {
    const { client, issuer, store } = setup({ stored: clock => session(clock, { access: 0 }) });
    store.clearError = new Error('clear failed');
    const pending = client.acquireToken();
    await waitFor(() => issuer.calls.length === 1);
    issuer.answer({ kind: 'rejected', reason: 'InvalidGrant' });
    await assert.rejects(pending, /failed to clear the session/);
});

test('a store clear failure rejects clear', async () => {
    const { client, store } = setup();
    store.clearError = new Error('clear failed');
    await assert.rejects(client.clear(), /failed to clear the session/);
});

test('refuses an issuer with an unknown or missing contractVersion', () => {
    const store = memoryStore(null);
    for (const contractVersion of [2, 0, '1', undefined]) {
        const issuer = Object.assign(controlledIssuer(), { contractVersion });
        assert.throws(() => new TokenClient({ store, issuer }), error =>
            error instanceof TypeError && /contract version/.test(error.message) && /supports only version 1/.test(error.message));
    }
    assert.equal(TokenClient.issuerContractVersion, 1);
});

test('refuses an invalid configuration', () => {
    const store = memoryStore(null), issuer = controlledIssuer();
    const invalid = [
        undefined,
        {},
        { store, issuer: null },
        { store: { read() {}, write() {} }, issuer },
        { store, issuer: Object.assign(controlledIssuer(), { id: '' }) },
        { store, issuer: Object.assign(controlledIssuer(), { refresh: null }) },
        { store, issuer, skewSeconds: -1 },
        { store, issuer, skewSeconds: '60' },
        { store, issuer, backoffInitialSeconds: 0 },
        { store, issuer, backoffInitialSeconds: 10, backoffMaxSeconds: 5 },
        { store, issuer, timeoutSeconds: 0 },
        { store, issuer, transport: {} },
        { store, issuer, now: 5 }
    ];
    for (const options of invalid) assert.throws(() => new TokenClient(options), TypeError, JSON.stringify(options));
    assert.ok(new TokenClient({ store, issuer, skewSeconds: 0 }) instanceof TokenClient);
    assert.ok(TokenClient({ store, issuer }) instanceof TokenClient, 'Calling without new still creates a client.');
    assert.throws(() => new TokenClient({ store, issuer }).acquireToken('not a function'), TypeError);
});

test('an adapter that throws rejects acquireToken', async () => {
    const { client, issuer } = setup({ stored: clock => session(clock, { access: 0 }) });
    issuer.refresh = () => { throw new Error('adapter bug'); };
    await assert.rejects(client.acquireToken(), /adapter bug/);
    // The failed operation does not block the next caller.
    issuer.refresh = (current, transport, callback) => callback({ kind: 'rejected', reason: 'InvalidGrant' });
    assert.equal((await client.acquireToken()).reason, 'InvalidGrant');
});

test('an adapter result with an unknown kind or an incomplete session rejects acquireToken', async () => {
    for (const result of [undefined, null, { kind: 'other' }, { kind: 'session', session: { accessToken: 'a' } },
        { kind: 'session', session: { accessToken: 'a', accessExpiresAt: '1', refreshToken: 'r' } }]) {
        const { client, issuer } = setup({ stored: clock => session(clock, { access: 0 }) });
        const pending = client.acquireToken();
        await waitFor(() => issuer.calls.length === 1);
        issuer.answer(result);
        await assert.rejects(pending, TypeError, JSON.stringify(result));
    }
});

test('an adapter that answers twice is heard once', async () => {
    const { client, issuer, store, clock } = setup({ stored: clock => session(clock, { access: 0 }) });
    const pending = client.acquireToken();
    await waitFor(() => issuer.calls.length === 1);
    issuer.answer({ kind: 'session', session: session(clock, { n: 2 }) });
    issuer.answer({ kind: 'rejected', reason: 'InvalidGrant' });
    assert.equal((await pending).token, 'access-2');
    await ticks();
    assert.equal(store.clears, 0);
});

test('an adapter that never answers is treated as unavailable after the timeout', async () => {
    const { client, issuer } = setup({ stored: clock => session(clock, { access: 0 }), options: { timeoutSeconds: 0.01 } });
    const started = Date.now();
    const outcome = await client.acquireToken();
    assert.equal(issuer.calls.length, 1);
    assert.equal(outcome.kind, 'temporarilyUnavailable');
    assert.ok(Date.now() - started >= 1000, 'The client gave up before the timeout and its grace period.');
});

test('the transport given to the adapter applies the client timeout and answers once', async () => {
    const requests = [];
    const transport = (request, callback) => {
        requests.push(request);
        callback(null, { status: 200, body: '{}' });
        callback(new Error('second answer'));
    };
    const { client, issuer, clock } = setup({ stored: clock => session(clock, { access: 0 }), options: { transport, timeoutSeconds: 5 } });
    const pending = client.acquireToken();
    await waitFor(() => issuer.calls.length === 1);
    const answers = [];
    const request = { method: 'POST', url: '/refresh' };
    issuer.calls[0].transport(request, (error, response) => answers.push([error, response]));
    assert.deepEqual(requests, [{ method: 'POST', url: '/refresh', timeout: 5000 }]);
    assert.equal(request.timeout, undefined, 'The adapter request object was changed.');
    assert.equal(answers.length, 1);
    assert.equal(answers[0][0], null);
    issuer.answer({ kind: 'session', session: session(clock, { n: 2 }) });
    await pending;
});

test('a callback receives (null, outcome) after acquireToken returns, and the Promise resolves as well', async () => {
    const { client } = setup({ stored: clock => session(clock, { access: 300 }) });
    let returned = false;
    let received = null;
    const promise = client.acquireToken((error, outcome) => { received = { error, outcome, afterReturn: returned }; });
    returned = true;
    const outcome = await promise;
    assert.equal(received.error, null);
    assert.equal(received.afterReturn, true);
    assert.equal(received.outcome, outcome);
    assert.equal(outcome.token, 'access-1');
});

test('a callback receives a programmer error, and the returned Promise is not reported as unhandled', async () => {
    const { client, issuer } = setup({ stored: clock => session(clock, { access: 0 }) });
    issuer.refresh = () => { throw new Error('adapter bug'); };
    const unhandled = [];
    const listener = reason => unhandled.push(reason);
    process.on('unhandledRejection', listener);
    try {
        const error = await new Promise(resolve => client.acquireToken(resolve));
        assert.match(error.message, /adapter bug/);
        await ticks();
        assert.deepEqual(unhandled, []);
    } finally {
        process.off('unhandledRejection', listener);
    }
});

test('clear accepts a callback', async () => {
    const { client, store } = setup({ stored: clock => session(clock) });
    const error = await new Promise(resolve => client.clear(resolve));
    assert.equal(error, null);
    assert.equal(store.value, null);
});

// Runs the source as a classic browser script in an isolated global, as old browsers would.
function browserGlobal(extra = {}) {
    const sandbox = Object.assign({ setTimeout, clearTimeout }, extra);
    sandbox.self = sandbox;
    vm.createContext(sandbox);
    return sandbox;
}

function run(context, file) {
    vm.runInContext(fs.readFileSync(path.join(SOURCE, file), 'utf8'), context, { filename: file });
}

test('without Promise, acquireToken works with a callback and returns nothing, and needs a callback', async () => {
    const context = browserGlobal();
    vm.runInContext('Promise = undefined;', context);
    run(context, 'shiftidentity-tokenclient.js');
    const clock = { t: T0 };
    const store = memoryStore(session(clock, { access: 300 }));
    const client = new context.ShiftIdentity.TokenClient({ store, issuer: controlledIssuer(), now: () => clock.t });
    assert.throws(() => client.acquireToken(), /pass a callback/);
    let returned;
    const outcome = await new Promise((resolve, reject) => {
        returned = client.acquireToken((error, value) => (error ? reject(error) : resolve(value)));
    });
    assert.equal(returned, undefined);
    assert.equal(outcome.token, 'access-1');
});

test('loads as a script global under window.ShiftIdentity, and the v2 adapter registers itself', () => {
    const context = browserGlobal({ ShiftIdentity: { Existing: true } });
    assert.throws(() => run(context, 'shiftidentity-tokenclient-issuer-v2.js'), /Load shiftidentity-tokenclient\.js before/);
    run(context, 'shiftidentity-tokenclient.js');
    run(context, 'shiftidentity-tokenclient-issuer-v2.js');
    const namespace = context.ShiftIdentity;
    assert.equal(namespace.Existing, true, 'The script replaced the existing namespace.');
    assert.equal(typeof namespace.TokenClient, 'function');
    assert.equal(typeof namespace.TokenClient.localStorageStore, 'function');
    assert.equal(typeof namespace.TokenClient.issuers.shiftIdentityV2, 'function');
    assert.equal(namespace.TokenClient.issuers.shiftIdentityV2({ baseUrl: '' }).contractVersion, 1);
});

test('loads as an AMD module', () => {
    const modules = {};
    function define(dependencies, factory) {
        const resolved = dependencies.map(name => modules[name]);
        modules[dependencies.length === 0 ? './shiftidentity-tokenclient' : 'issuer'] = factory.apply(null, resolved);
    }
    define.amd = {};
    const context = browserGlobal({ define });
    run(context, 'shiftidentity-tokenclient.js');
    run(context, 'shiftidentity-tokenclient-issuer-v2.js');
    assert.equal(context.ShiftIdentity, undefined, 'AMD loading also wrote a global.');
    assert.equal(typeof modules['./shiftidentity-tokenclient'], 'function');
    assert.equal(modules['./shiftidentity-tokenclient'].issuers.shiftIdentityV2, modules.issuer);
});

test('loads as CommonJS, and requiring the v2 adapter registers it on the core', () => {
    const shiftIdentityV2 = require('../src/shiftidentity-tokenclient-issuer-v2.js');
    assert.equal(TokenClient.issuers.shiftIdentityV2, shiftIdentityV2);
    assert.equal(shiftIdentityV2.id, 'shift-identity-v2');
    assert.equal(typeof TokenClient.xhrTransport, 'function');
});
