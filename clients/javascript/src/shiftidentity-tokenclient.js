/*
 * ShiftIdentity.TokenClient for browsers.
 *
 * Holds a session and hands out a usable access token. It refreshes the session when the access token is close
 * to expiry, runs one refresh at a time, and tells "sign in again" apart from "temporarily unavailable".
 * The issuer (the server that refreshes the session) plugs in through an adapter. See README.md.
 *
 * Plain ES5 with no dependencies. Load it with a <script> tag (it sets window.ShiftIdentity.TokenClient),
 * with CommonJS, or with AMD.
 */
(function (root, factory) {
    'use strict';
    if (typeof define === 'function' && define.amd) {
        define([], factory);
    } else if (typeof module === 'object' && module.exports) {
        module.exports = factory();
    } else {
        var namespace = root.ShiftIdentity = root.ShiftIdentity || {};
        namespace.TokenClient = factory();
    }
}(typeof self !== 'undefined' ? self : this, function () {
    'use strict';

    var NAME = 'ShiftIdentity.TokenClient';

    // The version of the issuer adapter contract that this core understands. An adapter declares the version it
    // implements, and the core refuses any other version.
    var CONTRACT_VERSION = 1;

    var DEFAULTS = {
        skewSeconds: 60,
        backoffInitialSeconds: 2,
        backoffMaxSeconds: 60,
        timeoutSeconds: 30
    };

    // The core waits this much longer than the request timeout before it stops waiting for an adapter that never
    // answers. A transport that honours the timeout answers first, so the adapter can map the failure itself.
    var WATCHDOG_GRACE_MS = 1000;

    // Browsers run a setTimeout with a longer delay than this at once, so longer delays are cut to it.
    var MAX_TIMER_MS = 2147483647;

    function noop() {}

    function hasOwn(value, key) {
        return Object.prototype.hasOwnProperty.call(value, key);
    }

    function isNonEmptyString(value) {
        return typeof value === 'string' && value.length > 0;
    }

    function isTime(value) {
        return typeof value === 'number' && isFinite(value);
    }

    function systemNow() {
        return new Date().getTime();
    }

    // Runs the action after the current call stack, so a callback never runs before the call that started it returns.
    function later(action) {
        setTimeout(action, 0);
    }

    function configurationError(message) {
        return new TypeError(NAME + ': ' + message);
    }

    function storeError(operation, cause) {
        var error = new Error(NAME + ': the credential store failed to ' + operation + ' the session.');
        error.cause = cause;
        return error;
    }

    // A waiter is one caller of acquireToken or clear. It is settled with a callback, a Promise, or both.
    function createWaiter(callback) {
        if (callback !== undefined && typeof callback !== 'function') {
            throw configurationError('the callback must be a function.');
        }
        var waiter = { callback: callback, promise: undefined, resolve: null, reject: null };
        if (typeof Promise === 'function') {
            waiter.promise = new Promise(function (resolve, reject) {
                waiter.resolve = resolve;
                waiter.reject = reject;
            });
            // The caller reads errors through the callback. Mark the Promise as handled so the runtime does not
            // also report the same error as an unhandled rejection.
            if (callback) waiter.promise.then(null, noop);
        } else if (!callback) {
            throw configurationError('pass a callback, because Promise is not available in this browser.');
        }
        return waiter;
    }

    function settle(waiter, error, value) {
        later(function () {
            if (error) {
                if (waiter.reject) waiter.reject(error);
            } else if (waiter.resolve) {
                waiter.resolve(value);
            }
            if (waiter.callback) waiter.callback(error || null, value);
        });
    }

    // Calls one store method. A store reports failure through its callback, but a store that throws is handled
    // the same way. A second call of the callback is ignored.
    function useStore(store, method, args, callback) {
        var called = false;
        function done(error, value) {
            if (called) return;
            called = true;
            callback(error, value);
        }
        try {
            store[method].apply(store, args.concat([done]));
        } catch (error) {
            // When the callback already ran, the error came from the code after it. Do not hide it.
            if (called) throw error;
            done(error || new Error('The store threw an empty error.'));
        }
    }

    // A stored session the core can work with: an object with an access token, a refresh token, or both.
    function readableSession(value) {
        if (!value || typeof value !== 'object') return null;
        if (!isNonEmptyString(value.accessToken) && !isNonEmptyString(value.refreshToken)) return null;
        return value;
    }

    // The session an adapter returns must be complete. Anything else is a bug in the adapter.
    function isCompleteSession(value) {
        return !!value && typeof value === 'object' &&
            isNonEmptyString(value.accessToken) && isTime(value.accessExpiresAt) &&
            isNonEmptyString(value.refreshToken) &&
            (value.refreshExpiresAt === null || value.refreshExpiresAt === undefined || isTime(value.refreshExpiresAt));
    }

    // Milliseconds of access left, or 0 when there is no usable access token.
    function accessLeft(session, now) {
        if (!isNonEmptyString(session.accessToken) || !isTime(session.accessExpiresAt)) return 0;
        return Math.max(0, session.accessExpiresAt - now);
    }

    // An unknown refresh expiry is not treated as expired. The issuer decides.
    function refreshExpired(session, now) {
        return isTime(session.refreshExpiresAt) && now >= session.refreshExpiresAt;
    }

    function tokenAvailable(session) {
        return {
            kind: 'tokenAvailable',
            token: session.accessToken,
            expiresAt: session.accessExpiresAt,
            extra: session.extra === undefined ? null : session.extra
        };
    }

    function signInRequired(reason, step) {
        var outcome = { kind: 'signInRequired', reason: reason };
        if (step !== undefined) outcome.step = step;
        return outcome;
    }

    function temporarilyUnavailable(delayMs) {
        return { kind: 'temporarilyUnavailable', retryAfter: Math.ceil(delayMs / 1000) };
    }

    function readSeconds(options, name, allowZero) {
        var value = options[name];
        if (value === undefined) return DEFAULTS[name];
        if (typeof value !== 'number' || !isFinite(value) || value < 0 || (!allowZero && value === 0)) {
            throw configurationError(name + ' must be a ' + (allowZero ? 'non-negative' : 'positive') + ' number of seconds.');
        }
        return value;
    }

    function validateStore(store) {
        if (!store || typeof store !== 'object' ||
            typeof store.read !== 'function' || typeof store.write !== 'function' || typeof store.clear !== 'function') {
            throw configurationError('store must be an object with read, write and clear functions.');
        }
    }

    function validateIssuer(issuer) {
        if (!issuer || typeof issuer !== 'object') throw configurationError('issuer is required.');
        if (!isNonEmptyString(issuer.id)) throw configurationError('issuer.id must be a non-empty string.');
        if (issuer.contractVersion !== CONTRACT_VERSION) {
            throw configurationError('issuer "' + issuer.id + '" implements adapter contract version ' +
                String(issuer.contractVersion) + ', but this client supports only version ' + CONTRACT_VERSION + '.');
        }
        if (typeof issuer.refresh !== 'function') throw configurationError('issuer "' + issuer.id + '" has no refresh function.');
    }

    // The default transport: one XMLHttpRequest.
    // request: { method, url, headers, body, timeout (ms) }.
    // callback(error) for a network failure or timeout, or callback(null, { status, body, header(name) }).
    function xhrTransport(request, callback) {
        var xhr = new XMLHttpRequest();
        var finished = false;
        var timer = null;
        function finish(error, response) {
            if (finished) return;
            finished = true;
            if (timer !== null) clearTimeout(timer);
            xhr.onreadystatechange = noop;
            callback(error, response);
        }
        function failure(message, timedOut) {
            var error = new Error(NAME + ': ' + message);
            error.timeout = timedOut;
            return error;
        }
        xhr.open(request.method, request.url, true);
        var headers = request.headers || {};
        for (var name in headers) {
            if (hasOwn(headers, name)) xhr.setRequestHeader(name, headers[name]);
        }
        xhr.onreadystatechange = function () {
            if (xhr.readyState !== 4) return;
            // Status 0 means no response arrived: offline, DNS, CORS, or an aborted request.
            if (xhr.status === 0) {
                finish(failure('the request failed before a response arrived.', false));
                return;
            }
            finish(null, {
                status: xhr.status,
                body: xhr.responseText,
                header: function (headerName) { return xhr.getResponseHeader(headerName); }
            });
        };
        // A timer is used instead of xhr.timeout, because older browsers do not support xhr.timeout.
        if (request.timeout > 0) {
            timer = setTimeout(function () {
                finish(failure('the request timed out.', true));
                try { xhr.abort(); } catch (ignored) { /* The request is already finished for the caller. */ }
            }, Math.min(request.timeout, MAX_TIMER_MS));
        }
        xhr.send(request.body === undefined ? null : request.body);
    }

    /**
     * new ShiftIdentity.TokenClient({ store, issuer, skewSeconds?, backoffInitialSeconds?, backoffMaxSeconds?,
     *                                 timeoutSeconds?, transport?, now? })
     * Throws a TypeError for an invalid configuration, including an issuer with an unknown contractVersion.
     */
    function TokenClient(options) {
        if (!(this instanceof TokenClient)) return new TokenClient(options);
        if (!options || typeof options !== 'object') throw configurationError('options are required.');
        validateStore(options.store);
        validateIssuer(options.issuer);
        if (options.transport !== undefined && typeof options.transport !== 'function') {
            throw configurationError('transport must be a function.');
        }
        if (options.now !== undefined && typeof options.now !== 'function') {
            throw configurationError('now must be a function that returns milliseconds since 1970.');
        }

        var backoffInitial = readSeconds(options, 'backoffInitialSeconds', false);
        var backoffMax = readSeconds(options, 'backoffMaxSeconds', false);
        if (backoffMax < backoffInitial) throw configurationError('backoffMaxSeconds must not be less than backoffInitialSeconds.');

        this._store = options.store;
        this._issuer = options.issuer;
        this._skewMs = readSeconds(options, 'skewSeconds', true) * 1000;
        this._backoffInitialMs = backoffInitial * 1000;
        this._backoffMaxMs = backoffMax * 1000;
        this._timeoutMs = readSeconds(options, 'timeoutSeconds', false) * 1000;
        this._now = options.now || systemNow;
        this._transport = boundTransport(options.transport || xhrTransport, this._timeoutMs);

        // clear() increases the generation. Work that started under an older generation is discarded when it ends.
        this._generation = 0;
        // The one running acquisition. Callers that arrive while it runs wait for its outcome.
        this._operation = null;
        this._failures = 0;
        this._retryAt = 0;
    }

    // Gives the adapter a transport that applies the client's timeout and answers at most once.
    function boundTransport(transport, timeoutMs) {
        return function (request, callback) {
            var copy = {};
            for (var key in request) {
                if (hasOwn(request, key)) copy[key] = request[key];
            }
            if (copy.timeout === undefined) copy.timeout = timeoutMs;
            var answered = false;
            transport(copy, function (error, response) {
                if (answered) return;
                answered = true;
                callback(error, response);
            });
        };
    }

    /**
     * Resolves one of:
     *   { kind: "tokenAvailable", token, expiresAt, extra }
     *   { kind: "signInRequired", reason, step? }
     *   { kind: "temporarilyUnavailable", retryAfter }   (seconds until the client calls the issuer again)
     * It rejects only for programmer errors: a failing store or a broken adapter.
     * Returns a Promise when Promise exists. The optional callback receives (error, outcome).
     */
    TokenClient.prototype.acquireToken = function (callback) {
        var waiter = createWaiter(callback);
        if (this._operation) {
            this._operation.waiters.push(waiter);
        } else {
            var operation = { generation: this._generation, waiters: [waiter] };
            this._operation = operation;
            this._start(operation);
        }
        return waiter.promise;
    };

    /**
     * Removes the session from the store and resets the retry delay. Callers still waiting for an acquisition
     * receive signInRequired with the reason "cleared", and a refresh that is still running is discarded when it ends.
     * Returns a Promise when Promise exists. The optional callback receives (error).
     */
    TokenClient.prototype.clear = function (callback) {
        var waiter = createWaiter(callback);
        var detached = [];
        this._generation += 1;
        this._resetBackoff();
        if (this._operation) {
            detached = this._operation.waiters;
            this._operation.waiters = [];
            this._operation = null;
        }
        useStore(this._store, 'clear', [], function (error) {
            var failure = error ? storeError('clear', error) : null;
            for (var i = 0; i < detached.length; i++) {
                settle(detached[i], failure, failure ? undefined : signInRequired('cleared'));
            }
            settle(waiter, failure, undefined);
        });
        return waiter.promise;
    };

    TokenClient.prototype._isStale = function (operation) {
        return operation.generation !== this._generation;
    };

    TokenClient.prototype._resetBackoff = function () {
        this._failures = 0;
        this._retryAt = 0;
    };

    TokenClient.prototype._finish = function (operation, error, outcome) {
        if (this._operation === operation) this._operation = null;
        var waiters = operation.waiters;
        operation.waiters = [];
        for (var i = 0; i < waiters.length; i++) settle(waiters[i], error, outcome);
    };

    TokenClient.prototype._start = function (operation) {
        var client = this;
        useStore(client._store, 'read', [], function (error, stored) {
            if (client._isStale(operation)) return;
            if (error) {
                client._finish(operation, storeError('read', error));
                return;
            }
            var session = readableSession(stored);
            if (!session) {
                client._finish(operation, null, signInRequired('noSession'));
                return;
            }
            var now = client._now();
            var left = accessLeft(session, now);
            // Refresh only when less than the skew is left. Exactly the skew left is still enough.
            if (left > 0 && left >= client._skewMs) {
                client._finish(operation, null, tokenAvailable(session));
                return;
            }
            // Without a refresh, the current access token is handed out while it lasts.
            if (!isNonEmptyString(session.refreshToken) || refreshExpired(session, now)) {
                client._finish(operation, null, left > 0 ? tokenAvailable(session) :
                    signInRequired(isNonEmptyString(session.refreshToken) ? 'refreshTokenExpired' : 'noRefreshToken'));
                return;
            }
            // The issuer was unavailable recently. Wait for the retry delay before asking it again.
            if (now < client._retryAt) {
                client._finish(operation, null, left > 0 ? tokenAvailable(session) :
                    temporarilyUnavailable(client._retryAt - now));
                return;
            }
            client._refresh(operation, session);
        });
    };

    TokenClient.prototype._refresh = function (operation, session) {
        var client = this;
        var answered = false;
        var watchdog = null;
        function answer(result) {
            if (answered) return;
            answered = true;
            if (watchdog !== null) clearTimeout(watchdog);
            if (client._isStale(operation)) return;
            client._apply(operation, session, result);
        }
        // An adapter or transport that never answers would block every later caller. Treat it as unavailable.
        watchdog = setTimeout(function () {
            watchdog = null;
            answer({ kind: 'unavailable' });
        }, Math.min(client._timeoutMs + WATCHDOG_GRACE_MS, MAX_TIMER_MS));
        try {
            client._issuer.refresh(session, client._transport, answer);
        } catch (error) {
            if (answered) throw error;
            answered = true;
            clearTimeout(watchdog);
            if (!client._isStale(operation)) client._finish(operation, error);
        }
    };

    TokenClient.prototype._apply = function (operation, current, result) {
        var client = this;
        var kind = result && typeof result === 'object' ? result.kind : undefined;

        if (kind === 'session') {
            if (!isCompleteSession(result.session)) {
                client._finish(operation, configurationError('issuer "' + client._issuer.id +
                    '" returned a session without accessToken, accessExpiresAt and refreshToken.'));
                return;
            }
            client._resetBackoff();
            // Success is reported only after the new session is stored.
            useStore(client._store, 'write', [result.session], function (error) {
                if (client._isStale(operation)) return;
                if (error) client._finish(operation, storeError('write', error));
                else client._finish(operation, null, tokenAvailable(result.session));
            });
        } else if (kind === 'rejected') {
            client._resetBackoff();
            useStore(client._store, 'clear', [], function (error) {
                if (client._isStale(operation)) return;
                if (error) client._finish(operation, storeError('clear', error));
                else client._finish(operation, null, signInRequired(isNonEmptyString(result.reason) ? result.reason : 'rejected', result.step));
            });
        } else if (kind === 'unavailable') {
            // The session stays in the store. Wait longer after each failure in a row, up to the maximum,
            // and never less than the issuer asked for.
            var now = client._now();
            client._failures += 1;
            var delay = Math.min(client._backoffMaxMs, client._backoffInitialMs * Math.pow(2, Math.min(client._failures - 1, 30)));
            if (isTime(result.retryAfter) && result.retryAfter * 1000 > delay) delay = result.retryAfter * 1000;
            client._retryAt = now + delay;
            client._finish(operation, null, accessLeft(current, now) > 0 ? tokenAvailable(current) : temporarilyUnavailable(delay));
        } else {
            client._finish(operation, configurationError('issuer "' + client._issuer.id + '" returned an unknown result.'));
        }
    };

    /**
     * A ready-made CredentialStore that keeps the session as JSON in localStorage under the given key.
     * The optional second argument replaces localStorage with another object that has getItem, setItem and removeItem.
     * Each operation takes effect when it is called, and its callback runs later.
     */
    TokenClient.localStorageStore = function (key, storage) {
        if (!isNonEmptyString(key)) throw configurationError('localStorageStore needs a non-empty key.');
        function area() {
            if (storage) return storage;
            if (typeof localStorage === 'undefined') throw new Error(NAME + ': localStorage is not available.');
            return localStorage;
        }
        function attempt(action, callback) {
            var error = null;
            var value;
            try {
                value = action();
            } catch (caught) {
                error = caught || new Error(NAME + ': localStorage threw an empty error.');
            }
            later(function () { (callback || noop)(error, value); });
        }
        return {
            read: function (callback) {
                attempt(function () {
                    var raw = area().getItem(key);
                    if (raw === null || raw === undefined) return null;
                    var value;
                    try {
                        value = JSON.parse(raw);
                    } catch (unreadable) {
                        // A damaged value is the same as no session.
                        return null;
                    }
                    return value && typeof value === 'object' ? value : null;
                }, callback);
            },
            write: function (session, callback) {
                attempt(function () { area().setItem(key, JSON.stringify(session)); }, callback);
            },
            clear: function (callback) {
                attempt(function () { area().removeItem(key); }, callback);
            }
        };
    };

    // Issuer adapter factories register here, for example issuers.shiftIdentityV2.
    TokenClient.issuers = {};
    TokenClient.issuerContractVersion = CONTRACT_VERSION;
    TokenClient.xhrTransport = xhrTransport;

    return TokenClient;
}));
