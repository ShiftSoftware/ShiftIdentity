# ShiftIdentity.TokenClient for JavaScript

A browser client that holds a session and hands out a usable access token. It refreshes the session when the
access token is close to expiry, runs one refresh at a time, and tells "sign in again" apart from "temporarily
unavailable". It never navigates and never runs a background timer. Call `acquireToken` before each request.

Plain ES5 with no dependencies and no build step. Each file works as a `<script>` (it sets
`window.ShiftIdentity`), as CommonJS, or as AMD.

| File | Contents |
|---|---|
| `src/shiftidentity-tokenclient.js` | The core: `TokenClient`, the `localStorage` store and the XHR transport. It does not know any issuer. |
| `src/shiftidentity-tokenclient-issuer-v2.js` | The ShiftIdentity v2 adapter. It registers `TokenClient.issuers.shiftIdentityV2`. |

## Use

```html
<script src="shiftidentity-tokenclient.js"></script>
<script src="shiftidentity-tokenclient-issuer-v2.js"></script>
<script>
  var TokenClient = ShiftIdentity.TokenClient;
  var client = new TokenClient({
    store: TokenClient.localStorageStore('my-app.identity-session'),
    issuer: TokenClient.issuers.shiftIdentityV2({ baseUrl: 'https://identity.example.com' })
  });

  client.acquireToken().then(function (outcome) {
    switch (outcome.kind) {
      case 'tokenAvailable': /* send "Authorization: Bearer " + outcome.token */ break;
      case 'signInRequired': /* the app decides: redirect, dialog or wait */ break;
      case 'temporarilyUnavailable': /* try again later; the session is kept */ break;
    }
  });
</script>
```

Every method also takes a Node-style callback, `function (error, outcome)`, and returns a Promise when `Promise`
exists. Without `Promise`, pass a callback.

### Outcomes

`acquireToken` always resolves with one of these. It rejects only for programmer errors: a store that fails, or
an adapter that throws or returns something invalid. Invalid options and an unknown adapter `contractVersion`
throw a `TypeError` from the constructor.

| `kind` | Fields | When |
|---|---|---|
| `tokenAvailable` | `token`, `expiresAt` (ms since 1970), `extra` | The access token has at least the skew left, or it was just refreshed. While the issuer is unavailable, or the refresh token has expired, an access token that has not expired yet is still handed out. |
| `signInRequired` | `reason`, `step` (optional) | No session (`noSession`), no refresh token (`noRefreshToken`), the refresh token has expired (`refreshTokenExpired`), `clear()` was called (`cleared`), or the issuer refused the session (the adapter's reason). After a refusal the store is cleared. |
| `temporarilyUnavailable` | `retryAfter` (seconds) | The issuer could not be reached, and there is no usable access token. The store is kept. The client does not call the issuer again until `retryAfter` has passed. |

`clear()` removes the session from the store. Callers still waiting get `signInRequired` with the reason
`cleared`, and a refresh that is still running is discarded when it ends.

### Options

| Option | Default | Meaning |
|---|---|---|
| `store` | required | A `CredentialStore` (below). |
| `issuer` | required | A `TokenIssuer` adapter (below). |
| `skewSeconds` | 60 | Refresh when less than this is left. Keep it below the access token lifetime, or every call refreshes. |
| `backoffInitialSeconds` | 2 | The wait after the first failed refresh. It doubles after each failure in a row. |
| `backoffMaxSeconds` | 60 | The longest wait. An issuer's `retryAfter` can ask for longer. |
| `timeoutSeconds` | 30 | The request timeout. An adapter that never answers is treated as unavailable shortly after it. |
| `transport` | XHR | `function (request, callback)`, see below. |
| `now` | `Date` | `function ()` that returns milliseconds since 1970. For tests. |

## Contracts

**Session** (`SessionCredentials`): `{ accessToken, accessExpiresAt, refreshToken, refreshExpiresAt, extra }`.
Expiry times are absolute, in milliseconds since 1970, computed with this device's clock when the session
arrives. `refreshExpiresAt` can be `null` (unknown). `extra` belongs to the adapter.

**CredentialStore**: `{ read(callback), write(session, callback), clear(callback) }`. Each callback takes
`(error)`, and `read`'s takes `(error, session or null)`. Operations must take effect in the order they are
called. `TokenClient.localStorageStore(key)` keeps the session as JSON in `localStorage`; it reads a damaged
value as no session and passes storage exceptions (for example a full quota) to the callback.

To store a new session (for example after a sign-in), write it to the store when no `acquireToken` call is
waiting, or call `clear()` first. The client reads the store on every call, so it picks up the new session.

**TokenIssuer** (adapter contract version 1): `{ id, contractVersion: 1, refresh(session, transport, callback) }`.
`refresh` calls `callback` once with one of:

- `{ kind: "session", session }`: a complete new session;
- `{ kind: "rejected", reason, step? }`: the session cannot be renewed, so the user must sign in again;
- `{ kind: "unavailable", retryAfter? }`: try again later (`retryAfter` in seconds).

The adapter owns the request and the mapping of the answer. The core owns everything else. Adapters for other
issuers ship from their own repositories and register under `TokenClient.issuers`. The core refuses any other
`contractVersion` (`TokenClient.issuerContractVersion` is the supported one).

**Transport**: `function (request, callback)`. `request` is `{ method, url, headers, body, timeout }`, with
`timeout` in milliseconds. The callback takes `(error)` for a network failure or timeout, or
`(null, { status, body, header(name) })`.

## The ShiftIdentity v2 adapter

`TokenClient.issuers.shiftIdentityV2({ baseUrl })` calls only `POST {baseUrl}/api/identity/v2/refresh` with
`{ "refreshToken": "..." }` and no `Authorization` header. Use `baseUrl: ''` for the page's own origin. A page on
another origin needs CORS on the API for a JSON `POST`, and a `Retry-After` header is only readable there when the
API exposes it.

| v2 answer | Adapter result |
|---|---|
| 2xx `kind: "session"` with an ordinary session (`flow` None, positive `tokenLifeTimeInSeconds`) | `session`, with `extra: { userData }` |
| `kind: "refused"` with any code except `Unavailable` and `AttemptsExhausted` (for example `InvalidGrant`, `Expired`, `ClientDenied`, `StaleOperation`, `AccountUnavailable`) | `rejected`, `reason` = the code name |
| `kind: "challenge"` (an outstanding step) | `rejected`, `reason: "challenge"`, `step` = the step name |
| Network failure, timeout, 5xx, 408, 429, `refused` with `Unavailable` or `AttemptsExhausted`, or any other answer | `unavailable` (with `retryAfter` from a `Retry-After` header in seconds) |

Codes and steps are read as numbers (the server default) or as names. Property names are read in any letter
case: a host with the framework's default JSON settings writes PascalCase (`Session`, `Code`, `Token`), and a
plain ASP.NET Core host writes camelCase. `extra.userData` is passed on as the host wrote it, so its property
names follow the host's letter case. `issuer.sessionFromToken(tokenDto)`
converts a `TokenDTO` from another v2 route, such as a device sign-in, into a session for the store.

## Tests

```bash
node --test
```

Run it in this folder. It needs no `npm install` (checked with Node 22). The tests use a fake store, transport
and clock. The v2 tests read the C# types in `ShiftIdentity.Core` and the routes in `ShiftIdentity.AspNetCore`, so
a renamed property or a new enum member makes them fail until the adapter is updated.
