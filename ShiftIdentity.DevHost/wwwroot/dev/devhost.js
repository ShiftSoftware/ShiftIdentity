// Helpers shared by the DevHost lab pages. Plain ES5, like the pages a TV runs.
var DevHost = (function () {
    'use strict';

    // Sends JSON and reads JSON. callback(status, body): status 0 means the request did not complete.
    function request(method, url, body, callback, headers) {
        var xhr = new XMLHttpRequest();
        xhr.open(method, url);
        xhr.timeout = 30000;
        if (body !== undefined) xhr.setRequestHeader('Content-Type', 'application/json');
        if (headers) for (var name in headers) if (headers.hasOwnProperty(name)) xhr.setRequestHeader(name, headers[name]);
        xhr.onload = function () {
            var parsed = null;
            try { parsed = xhr.responseText ? JSON.parse(xhr.responseText) : null; } catch (unreadable) { parsed = null; }
            callback(xhr.status, parsed);
        };
        xhr.onerror = function () { callback(0, null); };
        xhr.ontimeout = function () { callback(0, null); };
        xhr.send(body === undefined ? null : JSON.stringify(body));
    }

    // Reads a property in any letter case: a host with the framework's default JSON settings writes PascalCase
    // (Session, Code, Error), a plain ASP.NET Core host camelCase. Only "kind" is the same in both.
    function field(value, name) {
        if (!value || typeof value !== 'object') return undefined;
        if (value.hasOwnProperty(name)) return value[name];
        var lower = name.toLowerCase();
        for (var key in value) if (value.hasOwnProperty(key) && key.toLowerCase() === lower) return value[key];
        return undefined;
    }

    // The payload of a JWT, for display only. The page never trusts it: the server validates every token.
    function claims(token) {
        try {
            var part = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
            while (part.length % 4) part += '=';
            return JSON.parse(decodeURIComponent(escape(atob(part))));
        } catch (unreadable) {
            return null;
        }
    }

    function claim(payload, shortName, longName) {
        if (!payload) return undefined;
        return payload[shortName] !== undefined ? payload[shortName] : payload[longName];
    }

    function duration(seconds) {
        if (seconds === null || seconds === undefined || !isFinite(seconds)) return '—';
        var negative = seconds < 0;
        seconds = Math.abs(Math.round(seconds));
        var text = seconds >= 86400 ? Math.floor(seconds / 86400) + ' d ' + Math.floor(seconds % 86400 / 3600) + ' h'
            : seconds >= 3600 ? Math.floor(seconds / 3600) + ' h ' + Math.floor(seconds % 3600 / 60) + ' min'
            : seconds >= 60 ? Math.floor(seconds / 60) + ' min ' + seconds % 60 + ' s' : seconds + ' s';
        return negative ? 'expired ' + text + ' ago' : text;
    }

    function time() {
        var now = new Date();
        function two(value) { return value < 10 ? '0' + value : String(value); }
        return two(now.getHours()) + ':' + two(now.getMinutes()) + ':' + two(now.getSeconds());
    }

    // Adds a line at the top of a log element. tone: ok, warn or bad.
    function log(element, text, tone) {
        var line = document.createElement('div');
        var stamp = document.createElement('span');
        stamp.className = 'muted';
        stamp.textContent = time() + '  ';
        line.appendChild(stamp);
        var body = document.createElement('span');
        if (tone) body.className = 'pill ' + tone;
        body.textContent = text;
        line.appendChild(body);
        element.insertBefore(line, element.firstChild);
    }

    function text(tag, value, className) {
        var element = document.createElement(tag);
        element.textContent = value;
        if (className) element.className = className;
        return element;
    }

    // Fills a table body with the seeded accounts from /dev/info (username, whether it allows device sign-in, user ID,
    // current authenticator code, notes) and an element with their shared password. The accounts are read again when the codes change.
    // then(info), when given, runs after every read, for a page that shows more of /dev/info.
    function accounts(rows, password, then) {
        function load() {
            request('GET', '/dev/info', undefined, function (status, info) {
                if (status !== 200 || !info) { setTimeout(load, 5000); return; }
                password.textContent = info.password;
                rows.innerHTML = '';
                for (var i = 0; i < info.accounts.length; i++) {
                    var account = info.accounts[i];
                    var row = document.createElement('tr');
                    row.appendChild(text('td', account.username, 'mono'));
                    row.appendChild(text('td', account.deviceSignIn ? 'allowed' : '—', account.deviceSignIn ? 'ok' : ''));
                    row.appendChild(text('td', String(account.userID), 'mono'));
                    row.appendChild(text('td', account.code || '—', 'mono'));
                    row.appendChild(text('td', account.description));
                    rows.appendChild(row);
                }
                if (then) then(info);
                setTimeout(load, Math.max(1000, new Date(info.codeExpiresAt).getTime() - new Date().getTime() + 500));
            });
        }
        load();
    }

    return { request: request, field: field, claims: claims, claim: claim, duration: duration, log: log, text: text, accounts: accounts };
})();
