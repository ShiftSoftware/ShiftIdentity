// The token client lab: ShiftIdentity.TokenClient with the ShiftIdentity v2 adapter against this DevHost.
// It signs in through v2/login (PKCE), or takes the device simulator's session, and then shows what acquireToken
// answers while the access token ages, the refresh route fails, or the account changes. Plain ES5.
(function () {
    'use strict';

    var $ = function (id) { return document.getElementById(id); };
    var field = DevHost.field;
    var logElement = $('log');
    var TokenClient = window.ShiftIdentity && window.ShiftIdentity.TokenClient;
    var storeKey = 'devhost.lab-session';

    function log(text, tone) { DevHost.log(logElement, text, tone); }

    if (!TokenClient || !TokenClient.issuers.shiftIdentityV2) {
        log('The lab needs /clients/shiftidentity-tokenclient.js and its v2 adapter (clients/javascript/src).', 'bad');
        return;
    }

    var store = TokenClient.localStorageStore(storeKey);
    var issuer = TokenClient.issuers.shiftIdentityV2({ baseUrl: '' });
    // lastUserId: the account of the last session shown, so that Reactivate still works after a refusal emptied the store.
    var client = null, clientSkew = null, autoTimer = null, pendingMfa = null, lastToken = null, lastUserId = null;

    // Every refresh request the client sends goes through this transport, so the timeline shows each one. Five
    // concurrent acquireToken calls that need a refresh must show one request.
    function transport(request, callback) {
        log('→ ' + request.method + ' ' + request.url);
        TokenClient.xhrTransport(request, function (error, response) {
            log('← ' + (error ? 'no answer: ' + (error.message || error) : response.status + ' ' + (response.body || '').substring(0, 120)),
                error || response.status >= 400 ? 'warn' : null);
            callback(error, response);
        });
    }

    // A new skew needs a new client. The session stays in the store.
    function currentClient() {
        var skew = Math.max(0, Number($('skew').value) || 0);
        if (!client || clientSkew !== skew) {
            client = new TokenClient({ store: store, issuer: issuer, transport: transport, skewSeconds: skew });
            clientSkew = skew;
        }
        return client;
    }

    // ---- Getting a session ----

    function base64Url(bytes) {
        var text = '';
        for (var i = 0; i < bytes.length; i++) text += String.fromCharCode(bytes[i]);
        return btoa(text).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    }

    function pkce(callback) {
        var crypto = window.crypto;
        if (!crypto || !crypto.subtle || !crypto.getRandomValues) {
            log('PKCE needs crypto.subtle, which a browser offers only on loopback or HTTPS.', 'bad');
            return;
        }
        var random = new Uint8Array(32);
        crypto.getRandomValues(random);
        var verifier = base64Url(random);
        var ascii = new Uint8Array(verifier.length);
        for (var i = 0; i < verifier.length; i++) ascii[i] = verifier.charCodeAt(i);
        crypto.subtle.digest('SHA-256', ascii).then(function (hash) {
            callback(verifier, base64Url(new Uint8Array(hash)));
        });
    }

    function keep(tokenDto, source) {
        var session;
        try { session = issuer.sessionFromToken(tokenDto); } catch (refused) { log(refused.message, 'bad'); return; }
        // A new session is written while no acquireToken call waits: clear() first ends any that do.
        currentClient().clear(function () {
            store.write(session, function (error) {
                if (error) { log('The store refused the session: ' + error, 'bad'); return; }
                log('Session from ' + source + ' is in the store (localStorage "' + storeKey + '").', 'ok');
                render();
            });
        });
    }

    function answered(status, body, verifier) {
        var kind = field(body, 'kind');
        if (kind === 'session') { pendingMfa = null; $('mfaRow').hidden = true; keep(field(body, 'session'), 'v2/login'); return; }
        if (kind === 'challenge') {
            var challenge = field(body, 'challenge');
            var step = field(challenge, 'step');
            if ((step === 0 || step === 'ExistingMfa') && field(challenge, 'handle')) {
                pendingMfa = { handle: field(challenge, 'handle'), verifier: verifier };
                $('mfaRow').hidden = false;
                log('The account asks for its authenticator code.', 'warn');
                return;
            }
            log('The account owes another step (' + step + '). Finish it in the Identity app first.', 'bad');
            return;
        }
        log('v2/login: ' + status + ' ' + JSON.stringify(body), 'bad');
    }

    $('signIn').onclick = function () {
        pkce(function (verifier, challenge) {
            DevHost.request('POST', '/api/identity/v2/login',
                { username: $('username').value, password: $('password').value, codeChallenge: challenge },
                function (status, body) { answered(status, body, verifier); });
        });
    };

    $('mfa').onclick = function () {
        if (!pendingMfa) return;
        DevHost.request('POST', '/api/identity/v2/login/mfa', { code: $('mfaCode').value, codeVerifier: pendingMfa.verifier },
            function (status, body) { answered(status, body, pendingMfa && pendingMfa.verifier); },
            { 'Authorization': 'Operation ' + pendingMfa.handle });
    };

    $('fromDevice').onclick = function () {
        TokenClient.localStorageStore('devhost.device-session').read(function (error, session) {
            if (error || !session) { log('The device simulator holds no session. Turn its keep-alive on after it signs in.', 'warn'); return; }
            currentClient().clear(function () {
                store.write(session, function () { log('Copied the device simulator\'s session into the lab\'s store.', 'ok'); render(); });
            });
        });
    };

    $('clear').onclick = function () {
        currentClient().clear(function () { log('clear(): the store is empty, and waiting calls get signInRequired (cleared).'); render(); });
    };

    // ---- acquireToken ----

    function show(outcome) {
        var tone = outcome.kind === 'tokenAvailable' ? 'ok' : outcome.kind === 'signInRequired' ? 'bad' : 'warn';
        $('outcome').textContent = outcome.kind;
        $('outcome').className = 'pill ' + tone;
        var detail = outcome.kind === 'tokenAvailable' ? 'expires in ' + DevHost.duration((outcome.expiresAt - new Date().getTime()) / 1000)
            : outcome.kind === 'signInRequired' ? 'reason ' + outcome.reason + (outcome.step ? ', step ' + outcome.step : '')
            : 'retry after ' + outcome.retryAfter + ' s';
        $('outcomeDetail').textContent = detail;
        var renewed = outcome.kind === 'tokenAvailable' && lastToken && outcome.token !== lastToken;
        if (outcome.kind === 'tokenAvailable') lastToken = outcome.token;
        log('acquireToken → ' + outcome.kind + (renewed ? ' (a new access token)' : '') + ', ' + detail, renewed ? 'ok' : tone === 'ok' ? null : tone);
        render();
    }

    function acquire() {
        currentClient().acquireToken(function (error, outcome) {
            if (error) { log('acquireToken rejected (a programmer error): ' + error.message, 'bad'); return; }
            show(outcome);
        });
    }

    $('acquire').onclick = acquire;
    $('acquireMany').onclick = function () {
        log('Five acquireToken calls at once. When a refresh is due, the timeline shows one request for all five.');
        for (var i = 0; i < 5; i++) acquire();
    };
    $('auto').onchange = function () {
        if (autoTimer) clearInterval(autoTimer);
        autoTimer = this.checked ? setInterval(acquire, Math.max(1, Number($('autoSeconds').value) || 5) * 1000) : null;
    };

    // ---- Faults ----

    function edit(change, message) {
        store.read(function (error, session) {
            if (error || !session) { log('The store is empty.', 'warn'); return; }
            change(session);
            store.write(session, function () { log(message, 'warn'); render(); });
        });
    }

    $('expireAccess').onclick = function () {
        edit(function (session) { session.accessExpiresAt = new Date().getTime() - 1000; },
            'The stored access token now counts as expired. The next acquireToken refreshes.');
    };
    $('corrupt').onclick = function () {
        edit(function (session) { session.refreshToken = session.refreshToken.substring(0, session.refreshToken.length - 6) + 'AAAAAA'; },
            'The stored refresh token is corrupted. The next refresh is refused (InvalidGrant).');
    };

    var faults = document.getElementsByName('fault');
    for (var i = 0; i < faults.length; i++) {
        faults[i].onchange = function () {
            var mode = this.value;
            DevHost.request('POST', '/dev/faults/refresh/' + mode, {}, function (status) {
                log('Refresh route: ' + mode + (status === 200 ? '' : ' (DevHost answered ' + status + ')'), mode === 'none' ? 'ok' : 'warn');
            });
        };
    }

    function admin(change) {
        if (!lastUserId) { log('Get a session first.', 'warn'); return; }
        var id = lastUserId;
        DevHost.request('POST', '/dev/admin/users/' + id + '/' + change, {}, function (status, body) {
            log('DevHost ' + change + ' user ' + id + ': ' + status + ' ' + JSON.stringify(body), status === 200 ? 'ok' : 'bad');
        });
    }

    $('deactivate').onclick = function () { admin('deactivate'); };
    $('activate').onclick = function () { admin('activate'); };
    $('bump').onclick = function () { admin('bump-security-version'); };
    $('clearLog').onclick = function () { logElement.innerHTML = ''; };

    // ---- The stored session ----

    function render() {
        store.read(function (error, session) {
            $('empty').hidden = !!session;
            $('stored').hidden = !session;
            if (!session) return;
            var now = new Date().getTime();
            var payload = DevHost.claims(session.accessToken) || {};
            if (payload.shift_uid) lastUserId = payload.shift_uid;
            var name = DevHost.claim(payload, 'name', 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name');
            $('account').textContent = name + ' (user ' + payload.shift_uid + ', ' + payload.shift_client + ', ' + payload.shift_route + ')';
            var lifetime = payload.exp && payload.iat ? payload.exp - payload.iat : null;
            $('lifetime').textContent = lifetime ? lifetime + ' s' : '—';
            $('access').textContent = DevHost.duration((session.accessExpiresAt - now) / 1000);
            $('refreshInfo').textContent = session.refreshExpiresAt ? DevHost.duration((session.refreshExpiresAt - now) / 1000) : 'unknown';
            var skew = Math.max(0, Number($('skew').value) || 0);
            $('skewSeconds').textContent = skew;
            if (lifetime) {
                $('lifetimeBar').style.width = Math.max(0, Math.min(100, (session.accessExpiresAt - now) / 10 / lifetime)) + '%';
                $('skewMark').style.width = Math.min(100, skew * 100 / lifetime) + '%';
            }
        });
    }

    DevHost.request('GET', '/dev/faults', undefined, function (status, body) {
        var mode = field(body, 'refresh');
        for (var j = 0; j < faults.length; j++) faults[j].checked = faults[j].value === mode;
    });
    $('clientVersion').textContent = 'ShiftIdentity.TokenClient, adapter contract ' + TokenClient.issuerContractVersion;
    render();
    setInterval(render, 1000);
})();
