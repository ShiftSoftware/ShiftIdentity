# shift_identity_token_client

ShiftIdentity.TokenClient for Dart and Flutter. It holds a session and hands out a usable access token:

- it refreshes the session when the access token is close to expiry;
- it runs one refresh at a time, however many requests ask at once;
- it tells **"sign in again"** apart from **"temporarily unavailable"**, so a dropped connection never signs the user out;
- it never navigates and never runs a background timer. Your app decides what to show.

The package is pure Dart (its only dependency is `package:http`). It runs in Flutter on every platform, including
the web, and also in command-line and server code.

For Flutter, add [`shift_identity_token_client_flutter`](https://pub.dev/packages/shift_identity_token_client_flutter)
as well. It adds `SecureCredentialStore`, which keeps the session in the Keychain on iOS and macOS and in encrypted
storage on Android. Its README walks through a complete app.

## Install

```bash
dart pub add shift_identity_token_client
```

Requires Dart 3.4 or later.

## Quick start

```dart
import 'package:shift_identity_token_client/shift_identity_token_client.dart';

final issuer = ShiftIdentityV2Issuer(baseUrl: 'https://identity.example.com');
final tokens = TokenClient(store: InMemoryCredentialStore(), issuer: issuer);

// After your sign-in flow returns a ShiftIdentity v2 TokenDTO (as JSON):
await tokens.replaceSession(issuer.sessionFromToken(tokenDto));

// Before each request:
switch (await tokens.acquireToken()) {
  case TokenAvailable(:final token):
    // send 'Authorization: Bearer $token'
  case SignInRequired(:final reason):
    // the app decides: open the sign-in page, show a dialog, or wait
  case TemporarilyUnavailable(:final retryAfter):
    // try again later; the session is kept
}

// Sign out:
await tokens.clear();
```

Or let an `http.Client` attach the token for you:

```dart
final api = TokenHttpClient(
  tokens: tokens,
  apiBaseUrls: [Uri.parse('https://api.example.com/')],
);

final orders = await api.get(Uri.parse('https://api.example.com/orders'));
```

Create one `TokenClient` per signed-in session and issuer, and share it (for example through your dependency
injection or a top-level provider). Calls that arrive while a refresh runs wait for that refresh.

## Outcomes

`acquireToken()` always completes with exactly one `TokenOutcome`. Authentication conditions are never exceptions.

| Outcome | Fields | When |
|---|---|---|
| `TokenAvailable` | `token`, `expiresAt` (UTC), `extra` | The access token has at least the skew left, or it was just refreshed and stored. While the issuer is unavailable, or the refresh token has expired, an access token that has not expired yet is still handed out. |
| `SignInRequired` | `reason`, `step` (nullable) | No session (`noSession`), no refresh token (`noRefreshToken`), the refresh token has expired (`refreshTokenExpired`), `clear()` was called while waiting (`cleared`), or the issuer refused the session (the adapter's reason, such as `InvalidGrant`). After a refusal the store is cleared. |
| `TemporarilyUnavailable` | `retryAfter` | The issuer could not be reached and there is no usable access token. The store is kept. The client does not call the issuer again until `retryAfter` has passed. |

The constant reasons are in `SignInReasons`. With ShiftIdentity v2, an issuer refusal gives the
`AuthenticationFailure` name as the reason, and an outstanding step gives the reason `challenge` with the
`AuthenticationStep` name in `step` (for example `PasswordChange`). The app can then send the user to the
matching page.

`acquireToken()` throws only for programmer and platform errors:

- `CredentialStoreException`: the store failed to `read`, `write` or `clear` (for example a locked keychain);
- whatever an issuer adapter throws, or a `StateError` when an adapter returns an incomplete session.

## Options

| Option | Default | Meaning |
|---|---|---|
| `store` | required | A `CredentialStore` (below). |
| `issuer` | required | A `TokenIssuer` adapter, for example `ShiftIdentityV2Issuer`. |
| `skew` | 60 s | Refresh when less than this is left. Keep it below the access token lifetime, or every call refreshes. |
| `backoffInitial` | 2 s | The wait after the first failed refresh. It doubles after each failure in a row. |
| `backoffMax` | 60 s | The longest wait. An issuer's `retryAfter` can ask for longer. |
| `timeout` | 30 s | The refresh request timeout. An adapter that never answers is treated as unavailable one second after it. |
| `transport` | `HttpTokenTransport()` | Sends the adapter's requests (below). |
| `now` | `DateTime.now` | The clock. For tests. |

Invalid options, and an issuer with an unknown `contractVersion`, throw an `ArgumentError` from the constructor.
Call `close()` when you drop the client. It closes the default transport's HTTP client.

## Methods

| Method | What it does |
|---|---|
| `acquireToken()` | Returns a usable access token or says why there is none (above). |
| `replaceSession(session)` | Stores a new session, for example right after sign-in. A refresh that is still running is discarded when it ends, so it cannot overwrite the new session. Callers still waiting receive the outcome for the new session. |
| `clear()` | Signs out: removes the session from the store and resets the back-off. Callers still waiting receive `SignInRequired(cleared)`. |
| `close()` | Closes the default transport. |

The client reads the store on every call and keeps no copy in memory. A session that another part of the app wrote
to the store is picked up by the next call.

## Sign-in

This package keeps a session alive. It does not sign the user in. Run your sign-in flow (ShiftIdentity v2 login,
a device sign-in, or your own page), take the `TokenDTO` it returns, and hand it over:

```dart
final session = issuer.sessionFromToken(tokenDtoJson); // Map<String, Object?>
await tokens.replaceSession(session);
```

`sessionFromToken` accepts camelCase and PascalCase property names. It throws an `ArgumentError` when the value is
not an ordinary v2 session: it needs `token`, `refreshToken`, a positive `tokenLifeTimeInSeconds`, and `flow` None.
A session that is waiting for an MFA code or a password change is not an ordinary session. Finish that step first.

## Automatic HTTP: `TokenHttpClient`

`TokenHttpClient` is an `http.Client`. Use it anywhere an `http.Client` fits, including generated API clients that
accept one.

- **Which requests get the token** is decided by URL only. Give either `apiBaseUrls` (scheme, host and port must
  match, and the path must start with the base path on a segment boundary) or your own `isManaged(Uri)`. Every
  other request is sent untouched, so the token never leaks to a CDN or a third-party API.
- **A request that already has an `Authorization` header** is sent as it is.
- **No token:** the request is not sent. `SignInRequiredException` or `TokenTemporarilyUnavailableException` is
  thrown, both subclasses of `TokenAcquisitionException`. Catch it once, in a repository base class or an error
  handler, instead of at every call:

  ```dart
  try {
    return await api.get(url);
  } on SignInRequiredException {
    router.go('/sign-in');
    rethrow;
  } on TokenTemporarilyUnavailableException catch (e) {
    showSnackBar('Offline. Retrying in ${e.outcome.retryAfter.inSeconds} s.');
    rethrow;
  }
  ```

- **A `401` from your API** is returned as a normal response. It does not sign the user out. A revoked session is
  detected when the next refresh is refused, which happens no later than the access token lifetime.
- **`inner`**: the client that really sends requests (default: a new `http.Client`). Pass your own to add logging,
  retries or certificate pinning. `close()` closes only a client it created.

Using Dio or another HTTP stack? Call `acquireToken()` in its request interceptor. The switch in the quick start
works there as it is.

## Stores

A `CredentialStore` is where the app keeps the session. The app owns how and where it persists, and the client
decides when to read, write and clear.

| Store | Use |
|---|---|
| `SecureCredentialStore` (in `shift_identity_token_client_flutter`) | Flutter apps on phones and desktops. Keychain or encrypted storage. |
| `JsonCredentialStore(readString:, writeString:, deleteString:)` | Any storage that can hold one string: `shared_preferences`, a file, a database row. |
| `InMemoryCredentialStore` | Tests, or apps that must not keep a session across restarts. |

A refresh token is a long-lived credential. On a phone, prefer `SecureCredentialStore` over plain preferences.

To write your own store, implement three methods:

```dart
abstract interface class CredentialStore {
  Future<SessionCredentials?> read();   // null when nothing (readable) is stored
  Future<void> write(SessionCredentials session);
  Future<void> clear();
}
```

- Operations must take effect in the order they are called. (`JsonCredentialStore` does this for you.)
- `write` must complete only once the session is stored. The client reports a new token only after that.
- Report a failure by throwing. The client passes it on as a `CredentialStoreException`.
- `SessionCredentials.toJson()` and `SessionCredentials.tryFromJson()` give you the serialized form.

The JSON is the same shape the JavaScript client keeps in `localStorage`:
`{ accessToken, accessExpiresAt, refreshToken, refreshExpiresAt, extra }`, with times in milliseconds since 1970.
A Flutter web app and a JavaScript page can share one stored session.

## The ShiftIdentity v2 adapter

`ShiftIdentityV2Issuer(baseUrl: ...)` calls only `POST {baseUrl}/api/identity/v2/refresh` with
`{"refreshToken": "..."}` and no `Authorization` header. A base URL with a path prefix
(`https://example.com/identity`) works. On the web, `baseUrl: ''` means the page's own origin.

| v2 answer | Result |
|---|---|
| 2xx `kind: "session"` with an ordinary session (`flow` None, positive `tokenLifeTimeInSeconds`) | new session, with `extra: {'userData': ...}` |
| `kind: "refused"` with any code except `Unavailable` and `AttemptsExhausted` (for example `InvalidGrant`, `Expired`, `ClientDenied`, `StaleOperation`, `AccountUnavailable`) | `SignInRequired`, `reason` = the code name |
| `kind: "challenge"` (an outstanding step) | `SignInRequired`, `reason: 'challenge'`, `step` = the step name |
| Network failure, timeout, 5xx, 408, 429, `refused` with `Unavailable` or `AttemptsExhausted`, or any other answer | unavailable (with `Retry-After` in seconds) |

Only a definite answer from the issuer signs the user out. Codes and steps are read as numbers (the server
default) or as names, and property names in any letter case. A host with the Shift framework's default JSON
settings writes PascalCase (`Session`, `Code`, `Token`), and a plain ASP.NET Core host writes camelCase.
`extra['userData']` is passed on as the host wrote it, so its property names follow the host's letter case.

Expiry times come from the lifetimes in the answer and this device's clock when the answer arrives, not from the
JWT's `exp` claim. A device clock that is off by a constant amount does not change when the client refreshes.

**Web:** a Flutter web app on another origin than the identity API needs CORS on the API for a JSON `POST`. A
`Retry-After` header is readable there only when the API sends `Access-Control-Expose-Headers: Retry-After`.

## Contracts for other issuers

The client is issuer-neutral. An adapter for another token issuer implements `TokenIssuer` and can ship from its
own package:

```dart
final class MyIssuer implements TokenIssuer {
  @override
  String get id => 'my-issuer';

  @override
  int get contractVersion => issuerContractVersion; // 1

  @override
  Future<IssuerResult> refresh(SessionCredentials session, TokenTransport transport) async {
    try {
      final response = await transport.send(TransportRequest(
        method: 'POST',
        url: Uri.parse('https://auth.example.com/refresh'),
        headers: const {'Content-Type': 'application/json'},
        body: jsonEncode({'refreshToken': session.refreshToken}),
      ));
      // Map the answer: IssuerSession(newSession), IssuerRejected(reason: ...),
      // or IssuerUnavailable(retryAfter: ...).
    } on TransportException {
      return const IssuerUnavailable();
    }
  }
}
```

- `IssuerSession(session)`: a complete new session (`accessToken`, `accessExpiresAt`, `refreshToken`).
- `IssuerRejected(reason:, step:)`: the session cannot be renewed. Use it only for a definite refusal.
- `IssuerUnavailable(retryAfter:)`: anything unclear. The session is kept.

The adapter owns the request and the mapping of the answer. The client owns everything else. The client refuses
an adapter whose `contractVersion` differs from `issuerContractVersion`.

**Transport:** `TokenTransport.send(TransportRequest)` returns a `TransportResponse` for any status code and throws
a `TransportException` when no response arrives in time. The default `HttpTokenTransport` uses `package:http`.
Pass your own `http.Client` to it (`HttpTokenTransport(myClient)`), or implement `TokenTransport` to route
refreshes through another HTTP stack. The client fills in its `timeout` on each request.

## Behaviour notes

- **Single flight.** Concurrent `acquireToken` calls share one store read and at most one refresh.
- **Write before success.** A new token is reported only after the store finished writing the new session.
- **Generation counter.** A refresh that finishes after `clear()` or `replaceSession()` is discarded. It cannot
  bring back a signed-out session.
- **Back-off.** After a failed refresh the client waits `backoffInitial`, doubling up to `backoffMax`, or the
  issuer's `retryAfter` when that is longer. A success, a refusal, `clear()` and `replaceSession()` reset it.
- **Expired refresh token.** It returns `SignInRequired(refreshTokenExpired)` but keeps the store. A device clock
  that jumps forward can make a good session look expired, and the next sign-in overwrites it anyway.
- **Watchdog.** An adapter that never answers is treated as unavailable shortly after `timeout`, so one lost
  request cannot block every later caller.
- **No background timer.** Call `acquireToken` (or use `TokenHttpClient`) when you need a token. An app that polls
  stays signed in by doing so.
- **Logging.** `toString()` of sessions and outcomes never prints a token.

## Testing your app

- Use `InMemoryCredentialStore` and a fake `TokenIssuer` that returns the results you want to test.
- Or keep the real `ShiftIdentityV2Issuer` and pass a `transport` that returns canned `TransportResponse`s.
- Pass `now` to control the clock.

## Related

- [`shift_identity_token_client_flutter`](https://pub.dev/packages/shift_identity_token_client_flutter): secure
  storage for Flutter, and a complete example app.
- The JavaScript client in the same repository (`clients/javascript`) implements the same contract for browsers.

## License

MIT. See [LICENSE](LICENSE).
