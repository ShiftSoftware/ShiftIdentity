'use strict';
// Tests of the default XMLHttpRequest transport with a fake XMLHttpRequest.
const { test } = require('node:test');
const assert = require('node:assert/strict');

const TokenClient = require('../src/shiftidentity-tokenclient.js');

function installFakeXhr() {
    const created = [];
    function FakeXhr() {
        this.headers = {};
        this.readyState = 0;
        this.status = 0;
        this.responseText = '';
        this.aborted = false;
        this.responseHeaders = {};
        created.push(this);
    }
    FakeXhr.prototype.open = function (method, url, async) { this.method = method; this.url = url; this.async = async; this.readyState = 1; };
    FakeXhr.prototype.setRequestHeader = function (name, value) { this.headers[name] = value; };
    FakeXhr.prototype.send = function (body) { this.body = body; };
    FakeXhr.prototype.abort = function () {
        this.aborted = true;
        this.readyState = 4;
        this.status = 0;
        this.onreadystatechange();
    };
    FakeXhr.prototype.getResponseHeader = function (name) {
        const key = Object.keys(this.responseHeaders).find(header => header.toLowerCase() === name.toLowerCase());
        return key === undefined ? null : this.responseHeaders[key];
    };
    FakeXhr.prototype.respond = function (status, body, headers = {}) {
        this.status = status;
        this.responseText = body;
        this.responseHeaders = headers;
        this.readyState = 2;
        this.onreadystatechange();
        this.readyState = 4;
        this.onreadystatechange();
    };
    global.XMLHttpRequest = FakeXhr;
    return created;
}

function send(request) {
    let resolve;
    const answered = new Promise(done => { resolve = done; });
    const answers = [];
    TokenClient.xhrTransport(request, (error, response) => { answers.push({ error, response }); resolve(); });
    return { answered, answers };
}

test('sends an asynchronous request with the method, URL, headers and body', async () => {
    const created = installFakeXhr();
    try {
        const { answered, answers } = send({
            method: 'POST', url: 'https://identity.example.test/api/identity/v2/refresh',
            headers: { 'Content-Type': 'application/json' }, body: '{"refreshToken":"r"}', timeout: 1000
        });
        const xhr = created[0];
        assert.equal(xhr.method, 'POST');
        assert.equal(xhr.url, 'https://identity.example.test/api/identity/v2/refresh');
        assert.equal(xhr.async, true);
        assert.deepEqual(xhr.headers, { 'Content-Type': 'application/json' });
        assert.equal(xhr.body, '{"refreshToken":"r"}');

        xhr.respond(503, '{"kind":"refused","code":8}', { 'Retry-After': '30' });
        await answered;
        assert.equal(answers.length, 1);
        assert.equal(answers[0].error, null);
        assert.equal(answers[0].response.status, 503);
        assert.equal(answers[0].response.body, '{"kind":"refused","code":8}');
        assert.equal(answers[0].response.header('retry-after'), '30');
    } finally {
        delete global.XMLHttpRequest;
    }
});

test('status 0 is a network failure', async () => {
    const created = installFakeXhr();
    try {
        const { answered, answers } = send({ method: 'POST', url: '/x', timeout: 1000 });
        created[0].respond(0, '');
        await answered;
        assert.match(answers[0].error.message, /failed before a response arrived/);
        assert.equal(answers[0].error.timeout, false);
    } finally {
        delete global.XMLHttpRequest;
    }
});

test('a timeout fails the request once and aborts it', async () => {
    const created = installFakeXhr();
    try {
        const { answered, answers } = send({ method: 'POST', url: '/x', timeout: 5 });
        await answered;
        await new Promise(resolve => setTimeout(resolve, 10));
        assert.equal(answers.length, 1);
        assert.equal(answers[0].error.timeout, true);
        assert.equal(created[0].aborted, true);
    } finally {
        delete global.XMLHttpRequest;
    }
});

test('a request without a body sends null', () => {
    const created = installFakeXhr();
    try {
        send({ method: 'GET', url: '/x' });
        assert.equal(created[0].body, null);
        created[0].respond(200, '');
    } finally {
        delete global.XMLHttpRequest;
    }
});
