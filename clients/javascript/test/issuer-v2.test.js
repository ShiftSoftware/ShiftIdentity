'use strict';
// Tests of the ShiftIdentity v2 adapter. The recorded answers and the enum tables are checked against the C# source
// of the server, so a renamed property or a new enum member makes these tests fail.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const TokenClient = require('../src/shiftidentity-tokenclient.js');
const shiftIdentityV2 = require('../src/shiftidentity-tokenclient-issuer-v2.js');
const fixtures = require('./fixtures/v2-refresh-responses.json').responses;

const REPOSITORY = path.resolve(__dirname, '..', '..', '..');
function csharp(relative) {
    return fs.readFileSync(path.join(REPOSITORY, relative), 'utf8');
}
const AUTH_OUTCOME = csharp('ShiftIdentity.Core/Authentication/AuthOutcome.cs');
const TOKEN_DTO = csharp('ShiftIdentity.Core/DTOs/TokenDTO.cs');
const AUTH_PURPOSE = csharp('ShiftIdentity.Core/Enums/AuthPurpose.cs');
const ENDPOINTS = csharp('ShiftIdentity.AspNetCore/Endpoints/AdmissionEndpoints.cs');

const T0 = Date.UTC(2026, 9, 8, 10, 0, 0);
// Refusals that mean "try again later". Every other refusal ends the session.
const TRANSIENT = new Set(['Unavailable', 'AttemptsExhausted']);

function camel(name) {
    return name.charAt(0).toLowerCase() + name.slice(1);
}

function withoutLineComments(source) {
    return source.replace(/\/\/.*$/gm, '');
}

function enumMembers(source, name) {
    const match = withoutLineComments(source).match(new RegExp('enum\\s+' + name + '\\s*\\{([^}]*)\\}'));
    assert.ok(match, 'enum ' + name + ' was not found in the C# source.');
    return match[1].split(',').map(member => member.trim()).filter(Boolean);
}

// The positional parameters of a C# record: { name, omittedWhenNull }. A parameter with
// JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull) is not written when it is null.
function recordParameterDetails(source, name) {
    const match = source.match(new RegExp('record\\s+' + name + '\\s*\\(([\\s\\S]*?)\\)\\s*[:;{]'));
    assert.ok(match, 'record ' + name + ' was not found in the C# source.');
    return match[1].replace(/\[[^\]]*\]/g, attribute => (/WhenWritingNull/.test(attribute) ? ' @omittedWhenNull ' : ''))
        .split(',')
        .map(parameter => parameter.replace(/=[\s\S]*$/, '').trim())
        .filter(Boolean)
        .map(parameter => ({ name: parameter.match(/(\w+)$/)[1], omittedWhenNull: parameter.includes('@omittedWhenNull') }));
}

function recordParameters(source, name) {
    return recordParameterDetails(source, name).map(parameter => parameter.name);
}

// The names a parameter that is null still writes, in the fixture's letter case.
function writtenWhenNull(source, name, wire) {
    return recordParameterDetails(source, name).filter(parameter => !parameter.omittedWhenNull).map(parameter => wire(parameter.name));
}

// A C# property name as a fixture writes it.
function wireNaming(fixture) {
    return fixture.naming === 'PascalCase' ? name => name : camel;
}

function discriminators(source) {
    const map = {};
    for (const match of source.matchAll(/\[JsonDerivedType\(typeof\((\w+)\),\s*"(\w+)"\)\]/g)) map[match[1]] = match[2];
    return map;
}

const FAILURES = enumMembers(AUTH_OUTCOME, 'AuthenticationFailure');
const STEPS = enumMembers(AUTH_OUTCOME, 'AuthenticationStep');

function response(status, body, headers = {}) {
    return {
        status,
        body: typeof body === 'string' ? body : JSON.stringify(body),
        header(name) {
            const key = Object.keys(headers).find(header => header.toLowerCase() === name.toLowerCase());
            return key === undefined ? null : headers[key];
        }
    };
}

function recorded(name, headers) {
    return response(fixtures[name].status, fixtures[name].body, headers);
}

// Runs one adapter refresh against a fake transport. answer is a response, or { error } for a network failure.
function refresh(answer, { baseUrl = 'https://identity.example.test', now = () => T0, refreshToken = 'refresh-1' } = {}) {
    const requests = [];
    const issuer = shiftIdentityV2({ baseUrl, now });
    const current = { accessToken: 'access-1', accessExpiresAt: T0, refreshToken, refreshExpiresAt: null, extra: null };
    return new Promise(resolve => {
        issuer.refresh(current, (request, callback) => {
            requests.push(request);
            setImmediate(() => (answer.error ? callback(answer.error) : callback(null, answer)));
        }, result => resolve({ result, requests }));
    });
}

test('the recorded answers use the wire names of the C# types', () => {
    assert.equal(AUTH_OUTCOME.match(/JsonPolymorphic\(TypeDiscriminatorPropertyName\s*=\s*"(\w+)"\)/)[1], 'kind');
    const kinds = discriminators(AUTH_OUTCOME);
    assert.equal(kinds.SessionIssued, 'session');
    assert.equal(kinds.ChallengeRequired, 'challenge');
    assert.equal(kinds.AuthenticationRefused, 'refused');

    // { kind: "session", session: TokenDTO }
    assert.deepEqual(recordParameters(AUTH_OUTCOME, 'SessionIssued'), ['Session']);
    const tokenProperties = [...TOKEN_DTO.matchAll(/public\s+[\w<>?.]+\s+(\w+)\s*\{\s*get;/g)].map(match => match[1]);
    for (const name of ['Token', 'TokenLifeTimeInSeconds', 'RefreshToken', 'RefreshTokenLifeTimeInSeconds', 'Flow', 'UserData']) {
        assert.ok(tokenProperties.includes(name), 'TokenDTO has no ' + name + ', which the adapter reads.');
    }
    // { kind: "refused", code, passwordFailure }, and error only when it is set
    // { kind: "challenge", challenge: { step, handle, expiresAt, purpose, newAuthenticator } }
    assert.deepEqual(recordParameters(AUTH_OUTCOME, 'ChallengeRequired'), ['Challenge']);
    assert.equal(recordParameters(AUTH_OUTCOME, 'AuthenticationRefused')[0], 'Code');
    assert.equal(recordParameters(AUTH_OUTCOME, 'AuthenticationChallenge')[0], 'Step');

    const namings = new Set();
    for (const [name, fixture] of Object.entries(fixtures)) {
        const wire = wireNaming(fixture);
        namings.add(fixture.naming || 'camelCase');
        const label = name.replace(/(StringEnum|PascalCase)$/, '');
        if (fixture.body.kind === 'session') {
            assert.deepEqual(Object.keys(fixture.body), ['kind', wire('Session')], name);
            assert.deepEqual(Object.keys(fixture.body[wire('Session')]).sort(), tokenProperties.map(wire).sort(), name);
        }
        if (fixture.body.kind === 'refused') {
            assert.deepEqual(Object.keys(fixture.body), ['kind'].concat(writtenWhenNull(AUTH_OUTCOME, 'AuthenticationRefused', wire)), name);
            const code = fixture.body[wire('Code')];
            assert.equal(typeof code === 'number' ? FAILURES[code] : code, label.replace(/^refused/, ''), name + ' has the wrong code.');
        }
        if (fixture.body.kind === 'challenge') {
            const challenge = fixture.body[wire('Challenge')];
            assert.deepEqual(Object.keys(fixture.body), ['kind', wire('Challenge')], name);
            assert.deepEqual(Object.keys(challenge), recordParameters(AUTH_OUTCOME, 'AuthenticationChallenge').map(wire), name);
            assert.equal(STEPS[challenge[wire('Step')]], label.replace(/^challenge/, ''), name + ' has the wrong step.');
        }
    }
    assert.deepEqual([...namings].sort(), ['PascalCase', 'camelCase'], 'Both letter cases need recorded answers.');

    // The request body is RenewSessionRequest: { refreshToken }.
    assert.deepEqual(recordParameters(AUTH_OUTCOME, 'RenewSessionRequest').map(camel), ['refreshToken']);
    // The adapter accepts flow 0 only. 0 must be AuthPurpose.None.
    assert.match(enumMembers(AUTH_PURPOSE, 'AuthPurpose')[0], /^None(\s*=\s*0)?$/);
    // Numbers on the wire are positions, so the enums the adapter reads must not set their own values.
    for (const member of FAILURES.concat(STEPS)) assert.doesNotMatch(member, /=/, member + ' sets an explicit value.');
});

test('the recorded status codes and the route match the server endpoints', () => {
    assert.match(ENDPOINTS, /MapGroup\("\/api\/identity\/v2"\)/);
    assert.match(ENDPOINTS, /group\.MapPost\("\/refresh"/);
    assert.match(ENDPOINTS, /AuthenticationFailure\.Unavailable\s*\}\s*=>\s*503/);
    assert.match(ENDPOINTS, /AuthenticationFailure\.StaleOperation\s*\}\s*=>\s*409/);
    assert.match(ENDPOINTS, /AuthenticationRefused\s*=>\s*400/);
    assert.match(ENDPOINTS, /_\s*=>\s*200/);
    for (const [name, fixture] of Object.entries(fixtures)) {
        const code = fixture.body[wireNaming(fixture)('Code')];
        const failure = typeof code === 'number' ? FAILURES[code] : code;
        const expected = fixture.body.kind !== 'refused' ? 200 :
            failure === 'Unavailable' ? 503 : failure === 'StaleOperation' ? 409 : failure === 'ReauthenticationRequired' ? 403 : 400;
        assert.equal(fixture.status, expected, name);
    }
});

test('the adapter enum tables match AuthenticationFailure and AuthenticationStep, by number and by name', async () => {
    for (let index = 0; index < FAILURES.length; index++) {
        const name = FAILURES[index];
        const expected = TRANSIENT.has(name) ? { kind: 'unavailable' } : { kind: 'rejected', reason: name };
        for (const code of [index, name, camel(name)]) {
            const { result } = await refresh(response(400, { kind: 'refused', code, passwordFailure: null }));
            assert.deepEqual(result, expected, 'code ' + JSON.stringify(code));
        }
    }
    for (let index = 0; index < STEPS.length; index++) {
        for (const step of [index, STEPS[index]]) {
            const { result } = await refresh(response(200, { kind: 'challenge', challenge: { step, handle: null } }));
            assert.deepEqual(result, { kind: 'rejected', reason: 'challenge', step: STEPS[index] }, 'step ' + JSON.stringify(step));
        }
    }
});

test('maps every recorded v2 answer', async () => {
    const dto = fixtures.session.body.session;
    const pascal = fixtures.sessionPascalCase.body.Session;
    const table = [
        ['session', {
            kind: 'session',
            session: {
                accessToken: dto.token, accessExpiresAt: T0 + 900000, refreshToken: dto.refreshToken,
                refreshExpiresAt: T0 + 2592000000, extra: { userData: dto.userData }
            }
        }],
        ['sessionPascalCase', {
            kind: 'session',
            session: {
                accessToken: pascal.Token, accessExpiresAt: T0 + 900000, refreshToken: pascal.RefreshToken,
                refreshExpiresAt: T0 + 2592000000, extra: { userData: pascal.UserData }
            }
        }],
        ['refusedInvalidGrantPascalCase', { kind: 'rejected', reason: 'InvalidGrant' }],
        ['refusedUnavailablePascalCase', { kind: 'unavailable' }],
        ['challengePasswordChangePascalCase', { kind: 'rejected', reason: 'challenge', step: 'PasswordChange' }],
        ['refusedInvalidGrant', { kind: 'rejected', reason: 'InvalidGrant' }],
        ['refusedExpired', { kind: 'rejected', reason: 'Expired' }],
        ['refusedClientDenied', { kind: 'rejected', reason: 'ClientDenied' }],
        ['refusedStaleOperation', { kind: 'rejected', reason: 'StaleOperation' }],
        ['refusedAccountUnavailable', { kind: 'rejected', reason: 'AccountUnavailable' }],
        ['refusedInvalidRequest', { kind: 'rejected', reason: 'InvalidRequest' }],
        ['refusedInvalidGrantStringEnum', { kind: 'rejected', reason: 'InvalidGrant' }],
        ['refusedUnavailable', { kind: 'unavailable' }],
        ['challengePasswordChange', { kind: 'rejected', reason: 'challenge', step: 'PasswordChange' }],
        ['challengeNewMfa', { kind: 'rejected', reason: 'challenge', step: 'NewMfa' }]
    ];
    assert.deepEqual(table.map(row => row[0]).sort(), Object.keys(fixtures).sort(), 'A recorded answer has no expected result.');
    for (const [name, expected] of table) {
        const { result } = await refresh(recorded(name));
        assert.deepEqual(result, expected, name);
    }
});

test('network failures, timeouts and unclear answers are unavailable, never a rejection', async () => {
    const html = '<html><body>Bad Gateway</body></html>';
    const timeout = Object.assign(new Error('timed out'), { timeout: true });
    const cases = [
        ['network failure', { error: new Error('offline') }, { kind: 'unavailable' }],
        ['timeout', { error: timeout }, { kind: 'unavailable' }],
        ['503 with Retry-After', recorded('refusedUnavailable', { 'Retry-After': '30' }), { kind: 'unavailable', retryAfter: 30 }],
        ['503 with an HTTP-date Retry-After', recorded('refusedUnavailable', { 'Retry-After': 'Wed, 21 Oct 2026 07:28:00 GMT' }), { kind: 'unavailable' }],
        ['500 with a refusal body', response(500, fixtures.refusedInvalidGrant.body), { kind: 'unavailable' }],
        ['502 from a proxy', response(502, html), { kind: 'unavailable' }],
        ['504', response(504, ''), { kind: 'unavailable' }],
        ['429 with Retry-After', response(429, '', { 'retry-after': '5' }), { kind: 'unavailable', retryAfter: 5 }],
        ['408', response(408, ''), { kind: 'unavailable' }],
        ['status 0', response(0, ''), { kind: 'unavailable' }],
        ['404 from a wrong base URL', response(404, html), { kind: 'unavailable' }],
        ['401 without a v2 body', response(401, ''), { kind: 'unavailable' }],
        ['400 JSON without a kind', response(400, { title: 'Bad Request' }), { kind: 'unavailable' }],
        ['200 that is not JSON', response(200, html), { kind: 'unavailable' }],
        ['200 with another outcome kind', response(200, { kind: 'returnToLogin' }), { kind: 'unavailable' }],
        ['a session outside 2xx', response(400, fixtures.session.body), { kind: 'unavailable' }]
    ];
    for (const [name, answer, expected] of cases) {
        const { result } = await refresh(answer);
        assert.deepEqual(result, expected, name);
    }
});

test('a session that is not an ordinary v2 session is unavailable', async () => {
    const changes = {
        'no access lifetime': { tokenLifeTimeInSeconds: null },
        'a zero access lifetime': { tokenLifeTimeInSeconds: 0 },
        'no refresh token': { refreshToken: '' },
        'no access token': { token: null },
        'a restricted flow': { flow: 2 },
        'a restricted flow by name': { flow: 'Mfa' }
    };
    for (const [name, change] of Object.entries(changes)) {
        const body = { kind: 'session', session: Object.assign({}, fixtures.session.body.session, change) };
        const { result } = await refresh(response(200, body));
        assert.deepEqual(result, { kind: 'unavailable' }, name);
    }
});

test('an unknown refusal code is still a rejection', async () => {
    assert.deepEqual((await refresh(response(400, { kind: 'refused', code: 99 }))).result, { kind: 'rejected', reason: '99' });
    assert.deepEqual((await refresh(response(400, { kind: 'refused', code: 'SomethingNew' }))).result, { kind: 'rejected', reason: 'SomethingNew' });
    assert.deepEqual((await refresh(response(400, { kind: 'refused' }))).result, { kind: 'rejected', reason: 'Unknown' });
    assert.deepEqual((await refresh(response(200, { kind: 'challenge' }))).result, { kind: 'rejected', reason: 'challenge', step: 'Unknown' });
});

test('sends only POST /api/identity/v2/refresh with the refresh token as JSON and no Authorization header', async () => {
    const { requests } = await refresh(recorded('session'), { refreshToken: 'the-refresh-token' });
    assert.equal(requests.length, 1);
    assert.deepEqual(requests[0], {
        method: 'POST',
        url: 'https://identity.example.test/api/identity/v2/refresh',
        headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
        body: '{"refreshToken":"the-refresh-token"}'
    });

    const urls = {
        '': '/api/identity/v2/refresh',
        'https://identity.example.test/': 'https://identity.example.test/api/identity/v2/refresh',
        'https://identity.example.test//': 'https://identity.example.test/api/identity/v2/refresh',
        'https://example.test/identity': 'https://example.test/identity/api/identity/v2/refresh'
    };
    for (const [baseUrl, url] of Object.entries(urls)) {
        assert.equal((await refresh(recorded('session'), { baseUrl })).requests[0].url, url, baseUrl);
    }
});

test('session expiry times come from the lifetimes and the clock when the answer arrives', async () => {
    let time = T0;
    const now = () => time;
    const issuer = shiftIdentityV2({ baseUrl: '', now });
    const result = await new Promise(resolve => issuer.refresh({ refreshToken: 'r' }, (request, callback) => {
        time = T0 + 5000;
        callback(null, recorded('session'));
    }, resolve));
    assert.equal(result.session.accessExpiresAt, T0 + 5000 + 900000);
    assert.equal(result.session.refreshExpiresAt, T0 + 5000 + 2592000000);
});

test('sessionFromToken converts a TokenDTO from another v2 route into a session', () => {
    const issuer = shiftIdentityV2({ baseUrl: '', now: () => T0 });
    const dto = fixtures.session.body.session;
    assert.deepEqual(issuer.sessionFromToken(dto), {
        accessToken: dto.token, accessExpiresAt: T0 + 900000, refreshToken: dto.refreshToken,
        refreshExpiresAt: T0 + 2592000000, extra: { userData: dto.userData }
    });
    const unknownRefresh = issuer.sessionFromToken(Object.assign({}, dto, { refreshTokenLifeTimeInSeconds: null, flow: 'None', userData: undefined }));
    assert.equal(unknownRefresh.refreshExpiresAt, null);
    assert.deepEqual(unknownRefresh.extra, { userData: null });
    assert.ok(issuer.sessionFromToken(Object.assign({}, dto, { flow: undefined })));
    const pascal = fixtures.sessionPascalCase.body.Session;
    assert.equal(issuer.sessionFromToken(pascal).refreshToken, pascal.RefreshToken);

    for (const invalid of [null, {}, Object.assign({}, dto, { tokenLifeTimeInSeconds: -1 }), Object.assign({}, dto, { flow: 1 })]) {
        assert.throws(() => issuer.sessionFromToken(invalid), TypeError);
    }
});

test('the adapter declares contract version 1 and is accepted by the core', () => {
    const issuer = TokenClient.issuers.shiftIdentityV2({ baseUrl: 'https://identity.example.test' });
    assert.equal(issuer.id, 'shift-identity-v2');
    assert.equal(issuer.contractVersion, TokenClient.issuerContractVersion);
    const store = TokenClient.localStorageStore('k', { getItem: () => null, setItem() {}, removeItem() {} });
    assert.ok(new TokenClient({ store, issuer }));
});

test('the adapter refuses an invalid configuration', () => {
    for (const options of [undefined, {}, { baseUrl: 5 }, { baseUrl: null }, { baseUrl: '', now: 1 }]) {
        assert.throws(() => shiftIdentityV2(options), TypeError, JSON.stringify(options));
    }
});

test('end to end: a stored device session, a refresh inside the skew, an outage, then a refusal', async () => {
    const items = {};
    const storage = {
        getItem: key => (Object.prototype.hasOwnProperty.call(items, key) ? items[key] : null),
        setItem: (key, value) => { items[key] = String(value); },
        removeItem: key => { delete items[key]; }
    };
    let time = T0;
    const now = () => time;
    const issuer = TokenClient.issuers.shiftIdentityV2({ baseUrl: 'https://identity.example.test/', now });
    const store = TokenClient.localStorageStore('identity.session', storage);

    // Another v2 route (for example a device sign-in) hands over a TokenDTO. The app stores it.
    await new Promise((resolve, reject) =>
        store.write(issuer.sessionFromToken(fixtures.session.body.session), error => (error ? reject(error) : resolve())));

    const answers = [];
    const requests = [];
    const transport = (request, callback) => {
        requests.push(request);
        const answer = answers.shift();
        setImmediate(() => callback(null, answer));
    };
    const client = new TokenClient({ store, issuer, transport, now });

    const first = await client.acquireToken();
    assert.equal(first.token, fixtures.session.body.session.token);
    assert.equal(first.extra.userData.username, 'board-screen');
    assert.equal(requests.length, 0);

    // 50 s left is inside the default skew of 60 s. A host with the framework's default JSON settings answers.
    time = T0 + 850000;
    const renewed = JSON.parse(JSON.stringify(fixtures.sessionPascalCase.body));
    renewed.Session.Token = 'access-3';
    renewed.Session.RefreshToken = 'refresh-3';
    answers.push(response(200, renewed));
    const second = await client.acquireToken();
    assert.equal(second.token, 'access-3');
    assert.equal(second.expiresAt, time + 900000);
    assert.equal(requests[0].url, 'https://identity.example.test/api/identity/v2/refresh');
    assert.equal(requests[0].timeout, 30000);
    assert.equal(JSON.parse(requests[0].body).refreshToken, fixtures.session.body.session.refreshToken);
    assert.equal(JSON.parse(items['identity.session']).refreshToken, 'refresh-3');

    // The access token has expired and the issuer is down. The session stays.
    time += 900000;
    answers.push(recorded('refusedUnavailablePascalCase'));
    assert.deepEqual(await client.acquireToken(), { kind: 'temporarilyUnavailable', retryAfter: 2 });
    assert.ok(items['identity.session'], 'An unavailable issuer removed the session.');

    // The session is no longer valid. The issuer refuses, and the session is removed.
    time += 2000;
    answers.push(recorded('refusedInvalidGrantPascalCase'));
    assert.deepEqual(await client.acquireToken(), { kind: 'signInRequired', reason: 'InvalidGrant' });
    assert.equal(items['identity.session'], undefined);
    assert.equal(requests.length, 3);
});
