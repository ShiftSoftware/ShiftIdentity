'use strict';
// Tests of the ready-made localStorage CredentialStore, with a fake Web Storage object.
const { test } = require('node:test');
const assert = require('node:assert/strict');

const TokenClient = require('../src/shiftidentity-tokenclient.js');

function fakeStorage() {
    const storage = {
        items: {},
        failSet: null,
        failGet: null,
        getItem(key) {
            if (storage.failGet) throw storage.failGet;
            return Object.prototype.hasOwnProperty.call(storage.items, key) ? storage.items[key] : null;
        },
        setItem(key, value) {
            if (storage.failSet) throw storage.failSet;
            storage.items[key] = String(value);
        },
        removeItem(key) { delete storage.items[key]; }
    };
    return storage;
}

function call(store, method, ...args) {
    return new Promise(resolve => store[method](...args, (error, value) => resolve({ error, value })));
}

const SESSION = {
    accessToken: 'access-1', accessExpiresAt: 1000, refreshToken: 'refresh-1', refreshExpiresAt: 2000, extra: { userData: null }
};

test('writes, reads and clears the session as JSON under its key', async () => {
    const storage = fakeStorage();
    const store = TokenClient.localStorageStore('app.session', storage);

    assert.deepEqual(await call(store, 'read'), { error: null, value: null });
    assert.deepEqual(await call(store, 'write', SESSION), { error: null, value: undefined });
    assert.deepEqual(JSON.parse(storage.items['app.session']), SESSION);
    assert.deepEqual(await call(store, 'read'), { error: null, value: SESSION });
    assert.deepEqual(await call(store, 'clear'), { error: null, value: undefined });
    assert.equal(storage.items['app.session'], undefined);
});

test('a damaged or non-object value reads as no session', async () => {
    const storage = fakeStorage();
    const store = TokenClient.localStorageStore('k', storage);
    for (const raw of ['{not json', '"text"', '42', 'null']) {
        storage.items.k = raw;
        assert.deepEqual(await call(store, 'read'), { error: null, value: null }, raw);
    }
});

test('a storage exception is passed to the callback, not thrown', async () => {
    const storage = fakeStorage();
    const store = TokenClient.localStorageStore('k', storage);
    storage.failSet = new Error('QuotaExceededError');
    storage.failGet = new Error('SecurityError');
    assert.equal((await call(store, 'write', SESSION)).error, storage.failSet);
    assert.equal((await call(store, 'read')).error, storage.failGet);
});

test('callbacks run after the call returns, but the change is made at call time', async () => {
    const storage = fakeStorage();
    const store = TokenClient.localStorageStore('k', storage);
    let calledBack = false;
    const done = new Promise(resolve => store.write(SESSION, () => { calledBack = true; resolve(); }));
    assert.equal(calledBack, false);
    assert.ok(storage.items.k, 'The write was not made at call time.');
    await done;
    store.clear();
    assert.equal(storage.items.k, undefined, 'clear without a callback did not run.');
});

test('uses the global localStorage by default, and reports when there is none', async () => {
    assert.throws(() => TokenClient.localStorageStore(''), TypeError);
    assert.throws(() => TokenClient.localStorageStore(), TypeError);

    const store = TokenClient.localStorageStore('k');
    const missing = await call(store, 'read');
    assert.match(missing.error.message, /localStorage is not available/);

    const storage = fakeStorage();
    global.localStorage = storage;
    try {
        await call(store, 'write', SESSION);
        assert.ok(storage.items.k);
    } finally {
        delete global.localStorage;
    }
});

test('a failing localStorage write makes acquireToken reject after a refresh', async () => {
    const storage = fakeStorage();
    const now = 1000000;
    storage.items.k = JSON.stringify({ accessToken: 'a', accessExpiresAt: now, refreshToken: 'r', refreshExpiresAt: now + 60000 });
    const issuer = {
        id: 'test-issuer', contractVersion: 1,
        refresh(session, transport, callback) {
            callback({ kind: 'session', session: { accessToken: 'b', accessExpiresAt: now + 900000, refreshToken: 'r2', refreshExpiresAt: null } });
        }
    };
    const client = new TokenClient({ store: TokenClient.localStorageStore('k', storage), issuer, now: () => now });
    storage.failSet = new Error('QuotaExceededError');
    await assert.rejects(client.acquireToken(), error => error.cause === storage.failSet);

    storage.failSet = null;
    const outcome = await client.acquireToken();
    assert.equal(outcome.token, 'b');
    assert.equal(JSON.parse(storage.items.k).refreshToken, 'r2');
});
