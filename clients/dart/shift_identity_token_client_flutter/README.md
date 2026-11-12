# shift_identity_token_client_flutter

ShiftIdentity.TokenClient for Flutter. It keeps a ShiftIdentity session in the platform's secure storage and hands
out a valid access token:

- it refreshes the session when the access token is close to expiry, one refresh at a time;
- it tells **"sign in again"** apart from **"temporarily unavailable"**, so going offline never signs the user out;
- it attaches the token to your API requests (`TokenHttpClient`), and only to those;
- it never navigates. Your app decides what to show.

This package adds `SecureCredentialStore` to
[`shift_identity_token_client`](https://pub.dev/packages/shift_identity_token_client) and re-exports all of it, so
one import is enough. The full reference (outcomes, options, the v2 mapping, the store and adapter contracts) is in
[that package's README](https://pub.dev/packages/shift_identity_token_client).

## Install

```bash
flutter pub add shift_identity_token_client_flutter
```

Requires Flutter 3.22 (Dart 3.4) or later. `SecureCredentialStore` uses
[`flutter_secure_storage`](https://pub.dev/packages/flutter_secure_storage) (9.2 to 11.x) and inherits its
platform setup:

| Platform | Storage | Setup |
|---|---|---|
| Android | Encrypted storage backed by the Android Keystore | `minSdkVersion` as `flutter_secure_storage` requires. Exclude the store from Auto Backup, or a restored backup cannot be decrypted (see that package's README). |
| iOS, macOS | Keychain | macOS: add the Keychain Sharing entitlement. |
| Windows, Linux | DPAPI-protected file / libsecret | Linux: `libsecret-1-dev` at build time. |
| Web | Browser storage (encrypted with WebCrypto, not hardware-backed) | Serve over HTTPS. See the CORS note below. |

## Use

### 1. Create one client for the app

```dart
import 'package:shift_identity_token_client_flutter/shift_identity_token_client_flutter.dart';

final issuer = ShiftIdentityV2Issuer(baseUrl: 'https://identity.example.com');

final tokens = TokenClient(
  store: SecureCredentialStore(),
  issuer: issuer,
);

final api = TokenHttpClient(
  tokens: tokens,
  apiBaseUrls: [Uri.parse('https://api.example.com/')],
);
```

Provide `tokens` and `api` the way your app shares services: a top-level variable, `get_it`, `provider` or
`riverpod`. There must be one `TokenClient` per session, so concurrent requests share one refresh.

### 2. After sign-in, hand over the session

The package keeps a session alive. It does not sign the user in. When your sign-in flow (ShiftIdentity v2 login, a
device sign-in, or your own page) returns a v2 `TokenDTO`:

```dart
await tokens.replaceSession(issuer.sessionFromToken(tokenDtoJson));
```

### 3. Decide the start page

```dart
switch (await tokens.acquireToken()) {
  case TokenAvailable():
  case TemporarilyUnavailable(): // offline is not signed out
    router.go('/home');
  case SignInRequired():
    router.go('/sign-in');
}
```

### 4. Call your API

```dart
try {
  final response = await api.get(Uri.parse('https://api.example.com/orders'));
  // ...
} on SignInRequiredException catch (e) {
  // The session has ended: refused, expired, or signed out elsewhere. e.outcome.reason says why.
  router.go('/sign-in');
} on TokenTemporarilyUnavailableException catch (e) {
  // The identity server is unreachable and the access token has expired. The session is kept.
  showSnackBar('Offline. Retrying in ${e.outcome.retryAfter.inSeconds} s.');
}
```

Catch `TokenAcquisitionException` (the base of both) once, for example in a repository base class, rather than at
every call. With Dio or another HTTP stack, call `tokens.acquireToken()` in its request interceptor instead of
using `TokenHttpClient`.

When the issuer asks for an outstanding step, `SignInRequired.reason` is `challenge` and `step` names it (for
example `PasswordChange` or `NewMfa`). Send the user to the matching page.

### 5. Sign out

```dart
await tokens.clear();
```

## `SecureCredentialStore`

| Parameter | Default | Meaning |
|---|---|---|
| `key` | `shift_identity.session` | The secure storage key. Use a different key for each session kept at the same time (per issuer or per account). |
| `storage` | `const FlutterSecureStorage()` | Pass your own instance to set platform options. |

```dart
SecureCredentialStore(
  key: 'my_app.identity',
  storage: const FlutterSecureStorage(
    iOptions: IOSOptions(accessibility: KeychainAccessibility.first_unlock),
  ),
)
```

It keeps the session as JSON, reads a damaged value as no session, and runs operations in the order they are
called. A storage failure (for example a keychain that is locked before the first unlock) reaches your code as a
`CredentialStoreException` from `acquireToken`. It is a platform error, not a sign-out.

Two things to know about the platforms:

- **iOS keeps Keychain items after the app is uninstalled.** A reinstalled app finds the old session. If that is
  not what you want, clear it on the first launch. For example, keep a flag in `shared_preferences`, which is
  removed on uninstall, and call `tokens.clear()` when the flag is missing.
- **Background work** (for example a push notification handler on iOS) runs while the device may be locked. Use
  `KeychainAccessibility.first_unlock` if it needs a token then.

Prefer another storage? Any `CredentialStore` works. `JsonCredentialStore` from the core package wraps any
storage that holds one string, for example `shared_preferences`. A refresh token is a long-lived credential, so on
phones keep it in secure storage.

## Behaviour in short

- `acquireToken()` hands out the stored token while more than the skew (default 60 s) is left, and refreshes
  otherwise.
- A refused refresh clears the store and returns `SignInRequired` with the issuer's reason. Only a definite refusal
  signs the user out.
- An unreachable issuer keeps the session, still hands out an access token that has not expired, and backs off (2 s
  doubling to 60 s, or longer when the server sends `Retry-After`).
- No background timer: the token is refreshed when your app asks for one. `TokenHttpClient` asks before each
  managed request.
- A `401` from your API is not treated as a sign-out. A revoked session shows up when the next refresh is refused.

**Flutter web:** an app on another origin than the identity API needs CORS on the API for a JSON `POST`, and
`Access-Control-Expose-Headers: Retry-After` for the client to read the retry delay.

## Example

[`example/lib/main.dart`](example/lib/main.dart) is a complete app with a start page, a stand-in sign-in page, an
API call and sign-out. Set `identityBaseUrl` and `apiUrl`, run `flutter create .` in `example/` once to add the
platform folders, then `flutter run`.

## License

MIT. See [LICENSE](LICENSE).
