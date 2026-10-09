// The device simulator: a screen that signs in with device sign-in (RFC 8628) against this DevHost. It is plain ES5
// with XMLHttpRequest and no build step, so that it can serve as the reference for a TV page. The flow:
//   1. POST api/identity/v2/device/authorize { clientId } → deviceCode (secret), userCode, verificationUriComplete…
//   2. Show a QR code of verificationUriComplete, the short verificationUri and the userCode. Never show deviceCode.
//   3. Every `interval` seconds, POST api/identity/v2/device/token { deviceCode }:
//      authorization_pending → keep polling; slow_down → add 5 s; access_denied → stop;
//      expired_token or invalid_grant → ask for a new pair; temporarily_unavailable → try again later;
//      kind "session" → signed in. Store the session and renew it through api/identity/v2/refresh.
//   4. When the pair expires before anyone approves it, ask for a new one.
(function () {
    'use strict';

    var $ = function (id) { return document.getElementById(id); };
    var field = DevHost.field;
    var logElement = $('log');
    var TokenClient = window.ShiftIdentity && window.ShiftIdentity.TokenClient;
    var storeKey = 'devhost.device-session';

    // The current pair, the last device code that delivered a session (for the reuse check), the session the screen
    // holds, and the keep-alive client.
    var flow = null, consumedDeviceCode = null, session = null, pollTimer = null, restartTimer = null, keepAlive = null;

    // AuthenticationFailure in ShiftIdentity.Core/Authentication/AuthOutcome.cs, in order. The server writes it as a number.
    var FAILURES = ['InvalidRequest', 'InvalidProof', 'InvalidGrant', 'StaleOperation', 'Expired', 'AttemptsExhausted',
        'AccountUnavailable', 'ClientDenied', 'Unavailable', 'InvalidNewPassword', 'DuplicateIdentifier',
        'ReauthenticationRequired', 'ProviderAccountNotFound', 'ProviderEmailUnverified',
        'AuthorizationPending', 'SlowDown', 'AccessDenied', 'ExpiredToken'];

    function failureName(code) { return typeof code === 'number' && FAILURES[code] ? FAILURES[code] : String(code); }

    function describe(status, body) {
        if (status === 0) return 'no answer (network)';
        var kind = field(body, 'kind');
        if (kind === 'refused') {
            var error = field(body, 'error');
            return status + ' refused ' + failureName(field(body, 'code')) + (error ? ' (' + error + ')' : '');
        }
        return status + ' ' + (kind || 'unreadable answer');
    }

    function log(text, tone) { DevHost.log(logElement, text, tone); }

    function setStatus(text, tone, detail) {
        $('status').textContent = text;
        $('status').className = 'pill' + (tone ? ' ' + tone : '');
        $('statusDetail').textContent = detail || '';
    }

    function clearTimers() {
        if (pollTimer) clearTimeout(pollTimer);
        if (restartTimer) clearTimeout(restartTimer);
        pollTimer = restartTimer = null;
    }

    function stop() {
        clearTimers();
        flow = null;
        $('screen').hidden = true;
        setStatus('Idle');
    }

    function restartLater(seconds, why) {
        if (!$('auto').checked) return;
        log('Asking for a new code in ' + seconds + ' s: ' + why);
        restartTimer = setTimeout(start, seconds * 1000);
    }

    function start() {
        clearTimers();
        flow = null;
        var client = $('client').value;
        setStatus('Asking for a code…');
        DevHost.request('POST', '/api/identity/v2/device/authorize', { clientId: client }, function (status, body) {
            if (status !== 200 || field(body, 'kind') !== 'deviceAuthorizationStarted') {
                log('authorize ' + client + ': ' + describe(status, body), 'bad');
                setStatus('No code', 'bad', describe(status, body));
                if (status === 0 || status >= 500) restartLater(15, 'the service did not answer');
                return;
            }
            flow = {
                client: client,
                deviceCode: field(body, 'deviceCode'),
                userCode: field(body, 'userCode'),
                verificationUri: field(body, 'verificationUri'),
                complete: field(body, 'verificationUriComplete'),
                interval: field(body, 'interval'),
                expiresAt: new Date().getTime() + field(body, 'expiresIn') * 1000
            };
            log('authorize ' + client + ': code ' + flow.userCode + ', expires in ' + field(body, 'expiresIn') + ' s, interval ' + flow.interval + ' s', 'ok');
            showFlow();
            setStatus('Waiting for approval', 'warn');
            schedulePoll();
        });
    }

    function showFlow() {
        var qr = qrcode(0, 'M');
        qr.addData(flow.complete);
        qr.make();
        $('qr').innerHTML = qr.createSvgTag({ cellSize: 4, margin: 2, scalable: true, alt: 'QR code of ' + flow.complete });
        $('shortUrl').textContent = flow.verificationUri;
        $('userCode').textContent = flow.userCode;
        $('phoneLink').href = flow.complete;
        $('screen').hidden = false;
        tick();
    }

    function schedulePoll(seconds) {
        if (pollTimer) clearTimeout(pollTimer);
        pollTimer = setTimeout(poll, (seconds || flow.interval) * 1000);
    }

    function poll() {
        pollTimer = null;
        if (!flow) return;
        var current = flow;
        DevHost.request('POST', '/api/identity/v2/device/token', { deviceCode: current.deviceCode }, function (status, body) {
            if (flow !== current) return;
            handlePoll(status, body);
        });
    }

    function handlePoll(status, body) {
        var kind = field(body, 'kind');
        if (status === 200 && kind === 'session') {
            log('token: ' + describe(status, body), 'ok');
            consumedDeviceCode = flow.deviceCode;
            stop();
            receive(field(body, 'session'), 'device sign-in');
            return;
        }
        var error = field(body, 'error');
        switch (error) {
            case 'authorization_pending':
                log('token: ' + describe(status, body));
                setStatus('Waiting for approval', 'warn');
                schedulePoll();
                return;
            case 'slow_down':
                flow.interval += 5;
                log('token: ' + describe(status, body) + '; the interval is now ' + flow.interval + ' s', 'warn');
                schedulePoll();
                return;
            case 'access_denied':
                log('token: ' + describe(status, body), 'bad');
                stop();
                setStatus('Denied', 'bad', 'The request was denied, or the approving account can no longer sign in.');
                restartLater(10, 'the request was denied');
                return;
            case 'expired_token':
            case 'invalid_grant':
                log('token: ' + describe(status, body), 'warn');
                stop();
                setStatus('Code ended', 'warn', error);
                restartLater(1, 'the code ended');
                return;
            default:
                // temporarily_unavailable, a network failure or an unexpected answer: keep the code and try again later.
                log('token: ' + describe(status, body), 'warn');
                schedulePoll(Math.min(flow.interval * 2, 60));
        }
    }

    // A session arrived (from the device grant or a refresh). The screen keeps it and, with keep-alive on, hands it to
    // ShiftIdentity.TokenClient, which renews it on demand.
    function receive(tokenDto, source) {
        session = { token: field(tokenDto, 'token'), refreshToken: field(tokenDto, 'refreshToken'),
            accessExpiresAt: new Date().getTime() + field(tokenDto, 'tokenLifeTimeInSeconds') * 1000,
            refreshExpiresAt: new Date().getTime() + field(tokenDto, 'refreshTokenLifeTimeInSeconds') * 1000, source: source };
        showSession();
        setStatus('Signed in', 'ok', 'Session from ' + source + '.');
        if ($('keepAlive').checked) handToClient(tokenDto);
    }

    function showSession() {
        $('noSession').hidden = !!session;
        $('session').hidden = !session;
        if (!session) return;
        var payload = DevHost.claims(session.token);
        var name = DevHost.claim(payload, 'name', 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name');
        var fullName = DevHost.claim(payload, 'given_name', 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname');
        $('sessionUser').textContent = (fullName || '') + ' (' + (name || '?') + ')';
        $('sessionUserId').textContent = payload ? payload.shift_uid : '?';
        $('sessionContext').textContent = payload ? payload.shift_client + ' · ' + payload.shift_route + ' · mfa ' + payload.shift_mfa : '?';
        $('claims').textContent = JSON.stringify(payload, null, 2);
        tick();
    }

    function userId() {
        var payload = session && DevHost.claims(session.token);
        return payload && payload.shift_uid;
    }

    function tick() {
        var now = new Date().getTime();
        if (flow) {
            var left = (flow.expiresAt - now) / 1000;
            $('expires').textContent = DevHost.duration(left);
            $('interval').textContent = flow.interval;
            if (left <= 0 && !restartTimer) {
                log('The code ' + flow.userCode + ' expired on this screen before anyone approved it.', 'warn');
                stop();
                setStatus('Code expired', 'warn');
                restartLater(1, 'the code expired');
            }
        }
        if (session) {
            var payload = DevHost.claims(session.token);
            var exp = payload && payload.exp ? payload.exp * 1000 : session.accessExpiresAt;
            $('accessExpiry').textContent = DevHost.duration((exp - now) / 1000) + ' (lifetime from the response: ' +
                DevHost.duration((session.accessExpiresAt - now) / 1000) + ')';
            $('refreshExpiry').textContent = DevHost.duration((session.refreshExpiresAt - now) / 1000);
        }
    }

    function refreshNow() {
        if (!session) return;
        DevHost.request('POST', '/api/identity/v2/refresh', { refreshToken: session.refreshToken }, function (status, body) {
            if (status === 200 && field(body, 'kind') === 'session') {
                log('refresh: ' + describe(status, body), 'ok');
                receive(field(body, 'session'), 'a refresh');
            } else {
                log('refresh: ' + describe(status, body), 'bad');
            }
        });
    }

    function admin(change) {
        var id = userId();
        if (!id) return;
        DevHost.request('POST', '/dev/admin/users/' + id + '/' + change, {}, function (status, body) {
            log('DevHost ' + change + ' user ' + id + ': ' + status + ' ' + JSON.stringify(body), status === 200 ? 'ok' : 'bad');
        });
    }

    // Keep-alive: ShiftIdentity.TokenClient holds the session and renews it on demand. The screen asks it for a token
    // every few seconds, as a page that polls its own API would.
    function handToClient(tokenDto) {
        if (!TokenClient || !TokenClient.issuers.shiftIdentityV2) {
            log('Keep-alive needs /clients/shiftidentity-tokenclient.js and its v2 adapter.', 'bad');
            $('keepAlive').checked = false;
            return;
        }
        stopKeepAlive();
        var store = TokenClient.localStorageStore(storeKey);
        var issuer = TokenClient.issuers.shiftIdentityV2({ baseUrl: '' });
        var client = new TokenClient({ store: store, issuer: issuer });
        keepAlive = { client: client, timer: null };
        client.clear(function () {
            store.write(issuer.sessionFromToken(tokenDto), function (error) {
                if (error) { log('Keep-alive could not store the session: ' + error, 'bad'); return; }
                log('Keep-alive: the session is in ShiftIdentity.TokenClient (localStorage "' + storeKey + '").', 'ok');
                acquire();
            });
        });
    }

    function acquire() {
        if (!keepAlive) return;
        var current = keepAlive;
        current.client.acquireToken(function (error, outcome) {
            if (keepAlive !== current) return;
            if (error) { log('Keep-alive: programmer error: ' + error, 'bad'); return; }
            if (outcome.kind === 'tokenAvailable') {
                var renewed = session && outcome.token !== session.token;
                log('Keep-alive: tokenAvailable' + (renewed ? ' (renewed)' : '') + ', expires in ' +
                    DevHost.duration((outcome.expiresAt - new Date().getTime()) / 1000), renewed ? 'ok' : null);
                if (renewed) {
                    session.token = outcome.token;
                    session.accessExpiresAt = outcome.expiresAt;
                    showSession();
                }
            } else if (outcome.kind === 'signInRequired') {
                log('Keep-alive: signInRequired (' + outcome.reason + (outcome.step ? ', ' + outcome.step : '') + ')', 'bad');
                stopKeepAlive();
                $('keepAlive').checked = false;
                session = null;
                showSession();
                setStatus('Signed out', 'bad', 'The session cannot be renewed. Sign in again.');
                restartLater(3, 'the screen is signed out');
                return;
            } else {
                log('Keep-alive: temporarilyUnavailable, retry after ' + outcome.retryAfter + ' s', 'warn');
            }
            current.timer = setTimeout(acquire, Math.max(2, Number($('keepAliveSeconds').value) || 15) * 1000);
        });
    }

    function stopKeepAlive() {
        if (keepAlive && keepAlive.timer) clearTimeout(keepAlive.timer);
        keepAlive = null;
    }

    // Abuse checks. Each one logs the answer next to the answer it must get.
    function check(name, body, expected) {
        DevHost.request('POST', '/api/identity/v2/device/token', body, function (status, answer) {
            var error = field(answer, 'error');
            log(name + ': ' + describe(status, answer) + ' (expected ' + expected + ')', error === expected ? 'ok' : 'bad');
        });
    }

    $('start').onclick = start;
    $('stop').onclick = function () { stop(); log('Stopped.'); };
    $('refresh').onclick = refreshNow;
    $('deactivate').onclick = function () { admin('deactivate'); };
    $('activate').onclick = function () { admin('activate'); };
    $('bump').onclick = function () { admin('bump-security-version'); };
    $('disallow').onclick = function () { admin('disallow-device-sign-in'); };
    $('forget').onclick = function () {
        stopKeepAlive();
        $('keepAlive').checked = false;
        session = null;
        showSession();
        if (TokenClient) TokenClient.localStorageStore(storeKey).clear(function () { });
        log('The screen forgot its session (local only: the refresh token stays valid until it expires).');
    };
    $('keepAlive').onchange = function () {
        if (!this.checked) { stopKeepAlive(); log('Keep-alive off.'); return; }
        if (!session) { log('Keep-alive starts with the next session.'); return; }
        handToClient({ token: session.token, refreshToken: session.refreshToken,
            tokenLifeTimeInSeconds: Math.max(1, Math.round((session.accessExpiresAt - new Date().getTime()) / 1000)),
            refreshTokenLifeTimeInSeconds: Math.max(0, Math.round((session.refreshExpiresAt - new Date().getTime()) / 1000)) });
    };
    $('pollUserCode').onclick = function () {
        if (!flow) { log('Start a code first.', 'warn'); return; }
        check('poll with the user code ' + flow.userCode, { deviceCode: flow.userCode }, 'invalid_grant');
    };
    $('reuse').onclick = function () {
        if (!consumedDeviceCode) { log('Sign a screen in first, so that a device code has been used.', 'warn'); return; }
        check('reuse the consumed device code', { deviceCode: consumedDeviceCode }, 'invalid_grant');
    };
    $('fast').onclick = function () {
        if (!flow) { log('Start a code first.', 'warn'); return; }
        var current = flow;
        // Every slow_down adds 5 seconds on the server, so the screen adds 5 seconds for every one it receives.
        // A screen that skips one stays 5 seconds early and keeps hearing slow_down until the server's 60-second cap.
        function slowDown(error) {
            if (error === 'slow_down' && flow === current) { flow.interval += 5; schedulePoll(); }
        }
        DevHost.request('POST', '/api/identity/v2/device/token', { deviceCode: current.deviceCode }, function (status, body) {
            log('fast poll 1: ' + describe(status, body));
            slowDown(field(body, 'error'));
            DevHost.request('POST', '/api/identity/v2/device/token', { deviceCode: current.deviceCode }, function (second, answer) {
                var error = field(answer, 'error');
                log('fast poll 2: ' + describe(second, answer) + ' (expected slow_down)', error === 'slow_down' ? 'ok' : 'bad');
                slowDown(error);
            });
        });
    };
    $('expire').onclick = function () {
        if (!flow) { log('Start a code first.', 'warn'); return; }
        DevHost.request('POST', '/dev/admin/device-authorizations/expire', {}, function (status, body) {
            log('DevHost expired ' + field(body, 'expired') + ' code(s); the next poll must answer expired_token.', status === 200 ? 'ok' : 'bad');
            if (flow) { if (pollTimer) clearTimeout(pollTimer); poll(); }
        });
    };
    $('clearLog').onclick = function () { logElement.innerHTML = ''; };
    if (!TokenClient) $('keepAlive').disabled = true;
    DevHost.accounts($('accounts'), $('password'));
    setInterval(tick, 1000);
})();
