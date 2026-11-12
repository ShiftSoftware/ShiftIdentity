/// ShiftIdentity.TokenClient for Dart and Flutter.
///
/// Holds a session and hands out a usable access token. It refreshes the
/// session when the access token is close to expiry, runs one refresh at a
/// time, and tells "sign in again" apart from "temporarily unavailable".
///
/// * [TokenClient]: the client. Call [TokenClient.acquireToken] before a
///   request, or wrap your HTTP client in [TokenHttpClient].
/// * [ShiftIdentityV2Issuer]: renews sessions with ShiftIdentity v2.
/// * [CredentialStore]: where the session is kept. Ready-made:
///   [InMemoryCredentialStore], [JsonCredentialStore], and
///   `SecureCredentialStore` in `shift_identity_token_client_flutter`.
library;

export 'src/credential_store.dart';
export 'src/issuers/shift_identity_v2.dart';
export 'src/session_credentials.dart';
export 'src/token_client.dart';
export 'src/token_http_client.dart';
export 'src/token_issuer.dart';
export 'src/token_outcome.dart';
export 'src/transport.dart';
