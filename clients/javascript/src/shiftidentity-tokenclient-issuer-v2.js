/*
 * The ShiftIdentity v2 issuer adapter for ShiftIdentity.TokenClient.
 *
 * It calls only POST /api/identity/v2/refresh and maps the answer to the adapter contract:
 * { kind: "session", session }, { kind: "rejected", reason, step? } or { kind: "unavailable", retryAfter? }.
 *
 * Plain ES5 with no dependencies. Load shiftidentity-tokenclient.js first. This file registers
 * ShiftIdentity.TokenClient.issuers.shiftIdentityV2.
 */
(function (root, factory) {
    'use strict';
    function register(TokenClient) {
        var shiftIdentityV2 = factory();
        TokenClient.issuers.shiftIdentityV2 = shiftIdentityV2;
        return shiftIdentityV2;
    }
    if (typeof define === 'function' && define.amd) {
        define(['./shiftidentity-tokenclient'], register);
    } else if (typeof module === 'object' && module.exports) {
        module.exports = register(require('./shiftidentity-tokenclient.js'));
    } else {
        var namespace = root.ShiftIdentity;
        if (!namespace || !namespace.TokenClient) {
            throw new Error('Load shiftidentity-tokenclient.js before shiftidentity-tokenclient-issuer-v2.js.');
        }
        register(namespace.TokenClient);
    }
}(typeof self !== 'undefined' ? self : this, function () {
    'use strict';

    var NAME = 'ShiftIdentity.TokenClient.issuers.shiftIdentityV2';
    var ID = 'shift-identity-v2';
    var REFRESH_PATH = '/api/identity/v2/refresh';

    // The server writes these enums as numbers unless the host adds a string enum converter, so both forms are read.
    // The order must match AuthenticationFailure and AuthenticationStep in
    // ShiftIdentity.Core/Authentication/AuthOutcome.cs. A test compares them with that file.
    // The last four belong to the device sign-in route. The refresh route does not return them.
    var FAILURES = [
        'InvalidRequest', 'InvalidProof', 'InvalidGrant', 'StaleOperation', 'Expired', 'AttemptsExhausted',
        'AccountUnavailable', 'ClientDenied', 'Unavailable', 'InvalidNewPassword', 'DuplicateIdentifier',
        'ReauthenticationRequired', 'ProviderAccountNotFound', 'ProviderEmailUnverified',
        'AuthorizationPending', 'SlowDown', 'AccessDenied', 'ExpiredToken'
    ];
    var STEPS = ['ExistingMfa', 'PasswordChange', 'MfaRecovery', 'NewMfa', 'EmailVerification', 'Password'];

    // Refusals that mean "try again later". Every other refusal means the session cannot be renewed.
    var TRANSIENT_FAILURES = { Unavailable: true, AttemptsExhausted: true };

    function systemNow() {
        return new Date().getTime();
    }

    function configurationError(message) {
        return new TypeError(NAME + ': ' + message);
    }

    function isNonEmptyString(value) {
        return typeof value === 'string' && value.length > 0;
    }

    // Reads a property by name in any letter case. A host with the framework's default JSON settings
    // (ShiftEntity's JsonNamingPolicy is null) writes PascalCase names such as Session, Code and Token. A host with
    // the ASP.NET Core defaults writes camelCase. The "kind" discriminator is the same in both.
    function field(value, name) {
        if (!value || typeof value !== 'object') return undefined;
        if (Object.prototype.hasOwnProperty.call(value, name)) return value[name];
        var lower = name.toLowerCase();
        for (var key in value) {
            if (Object.prototype.hasOwnProperty.call(value, key) && key.toLowerCase() === lower) return value[key];
        }
        return undefined;
    }

    function trimTrailingSlashes(url) {
        while (url.length > 0 && url.charAt(url.length - 1) === '/') url = url.substring(0, url.length - 1);
        return url;
    }

    // Returns the enum member name for a number or a string in any letter case. An unknown value is returned as text.
    function enumName(names, value) {
        if (typeof value === 'number') {
            return value >= 0 && value % 1 === 0 && value < names.length ? names[value] : String(value);
        }
        if (typeof value === 'string') {
            var lower = value.toLowerCase();
            for (var i = 0; i < names.length; i++) {
                if (names[i].toLowerCase() === lower) return names[i];
            }
            return value;
        }
        return 'Unknown';
    }

    function parseJsonObject(body) {
        if (!isNonEmptyString(body)) return null;
        try {
            var value = JSON.parse(body);
            return value && typeof value === 'object' ? value : null;
        } catch (unreadable) {
            return null;
        }
    }

    // Converts a v2 TokenDTO into the core session shape, or returns null when it is not an ordinary session.
    // Expiry times come from the lifetimes and this device's clock at receipt, not from the token's own exp claim,
    // so a device clock that is wrong by a constant amount does not change when the client refreshes.
    function sessionFromTokenDto(token, receivedAt) {
        if (!token || typeof token !== 'object') return null;
        var accessToken = field(token, 'token');
        var refreshToken = field(token, 'refreshToken');
        if (!isNonEmptyString(accessToken) || !isNonEmptyString(refreshToken)) return null;
        var accessLifetime = field(token, 'tokenLifeTimeInSeconds');
        if (typeof accessLifetime !== 'number' || !isFinite(accessLifetime) || accessLifetime <= 0) return null;
        // Flow is AuthPurpose. Only None (0) is an ordinary session. The Blazor client checks the same.
        var flow = field(token, 'flow');
        if (flow !== undefined && flow !== null && flow !== 0 && String(flow).toLowerCase() !== 'none') return null;
        var refreshLifetime = field(token, 'refreshTokenLifeTimeInSeconds');
        var userData = field(token, 'userData');
        return {
            accessToken: accessToken,
            accessExpiresAt: receivedAt + accessLifetime * 1000,
            refreshToken: refreshToken,
            refreshExpiresAt: typeof refreshLifetime === 'number' && isFinite(refreshLifetime) && refreshLifetime >= 0 ?
                receivedAt + refreshLifetime * 1000 : null,
            // userData is passed on as the host wrote it, so its property names follow the host's letter case.
            extra: { userData: userData === undefined ? null : userData }
        };
    }

    // Reads a Retry-After header in seconds. The HTTP date form is ignored.
    function unavailable(response) {
        var result = { kind: 'unavailable' };
        var header = response && typeof response.header === 'function' ? response.header('Retry-After') : null;
        if (isNonEmptyString(header)) {
            var seconds = Number(header);
            if (isFinite(seconds) && seconds >= 0) result.retryAfter = seconds;
        }
        return result;
    }

    // Maps one HTTP answer of v2/refresh to an adapter result. Only a definite answer from the issuer
    // (a refusal or an outstanding step) means "sign in again". Anything unclear is treated as unavailable.
    function interpret(response, receivedAt) {
        var status = response && typeof response.status === 'number' ? response.status : 0;
        if (status === 0 || status >= 500 || status === 408 || status === 429) return unavailable(response);

        var outcome = parseJsonObject(response.body);
        var kind = field(outcome, 'kind');

        if (kind === 'session') {
            var session = status >= 200 && status < 300 ? sessionFromTokenDto(field(outcome, 'session'), receivedAt) : null;
            return session ? { kind: 'session', session: session } : unavailable(response);
        }
        if (kind === 'refused') {
            var code = enumName(FAILURES, field(outcome, 'code'));
            return TRANSIENT_FAILURES[code] === true ? unavailable(response) : { kind: 'rejected', reason: code };
        }
        if (kind === 'challenge') {
            return { kind: 'rejected', reason: 'challenge', step: enumName(STEPS, field(field(outcome, 'challenge'), 'step')) };
        }
        return unavailable(response);
    }

    /**
     * ShiftIdentity.TokenClient.issuers.shiftIdentityV2({ baseUrl, now? })
     * baseUrl is the ShiftIdentity API origin, for example "https://identity.example.com". Use "" for the page origin.
     */
    function shiftIdentityV2(options) {
        if (!options || typeof options !== 'object') throw configurationError('options with a baseUrl are required.');
        if (typeof options.baseUrl !== 'string') throw configurationError('baseUrl must be a string. Use "" for the page origin.');
        if (options.now !== undefined && typeof options.now !== 'function') {
            throw configurationError('now must be a function that returns milliseconds since 1970.');
        }
        var now = options.now || systemNow;
        var url = trimTrailingSlashes(options.baseUrl) + REFRESH_PATH;

        return {
            id: ID,
            contractVersion: 1,
            refresh: function (session, transport, callback) {
                transport({
                    method: 'POST',
                    url: url,
                    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                    body: JSON.stringify({ refreshToken: session.refreshToken })
                }, function (error, response) {
                    callback(error ? { kind: 'unavailable' } : interpret(response, now()));
                });
            },
            // Converts a TokenDTO from another v2 route (for example a device sign-in) into a session for the store.
            // Throws a TypeError when the value is not an ordinary v2 session.
            sessionFromToken: function (token) {
                var session = sessionFromTokenDto(token, now());
                if (!session) {
                    throw configurationError('the value is not an ordinary v2 session. It needs token, refreshToken, ' +
                        'a positive tokenLifeTimeInSeconds, and flow None.');
                }
                return session;
            }
        };
    }

    shiftIdentityV2.id = ID;

    return shiftIdentityV2;
}));
