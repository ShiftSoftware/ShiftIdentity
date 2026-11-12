/// What `TokenClient.acquireToken` resolves with. Exactly one of
/// [TokenAvailable], [SignInRequired] or [TemporarilyUnavailable].
///
/// ```dart
/// switch (await client.acquireToken()) {
///   case TokenAvailable(:final token):
///     // send 'Authorization: Bearer $token'
///   case SignInRequired(:final reason):
///     // the app decides: open the sign-in page, show a dialog, or wait
///   case TemporarilyUnavailable(:final retryAfter):
///     // try again later; the session is kept
/// }
/// ```
sealed class TokenOutcome {
  const TokenOutcome();
}

/// A usable access token.
///
/// The token has at least the skew left, or was just refreshed. While the
/// issuer is unavailable, or the refresh token has expired, an access token
/// that has not expired yet is still handed out.
final class TokenAvailable extends TokenOutcome {
  /// Creates the outcome.
  const TokenAvailable(
      {required this.token, required this.expiresAt, this.extra});

  /// The access token.
  final String token;

  /// When [token] expires (UTC, by this device's clock).
  final DateTime expiresAt;

  /// The issuer-specific data of the session (for the ShiftIdentity v2 adapter,
  /// `{'userData': ...}`), or `null`.
  final Object? extra;

  @override
  String toString() => 'TokenAvailable(expiresAt: $expiresAt)';
}

/// The user must sign in again. The client never navigates; the app decides
/// how to react: redirect, dialog, or wait.
///
/// After the issuer refuses the session, the store is cleared.
final class SignInRequired extends TokenOutcome {
  /// Creates the outcome.
  const SignInRequired({required this.reason, this.step});

  /// Why. One of [SignInReasons], or the reason the issuer adapter gave (for
  /// the ShiftIdentity v2 adapter, the `AuthenticationFailure` name such as
  /// `InvalidGrant`, or `challenge`).
  final String reason;

  /// For a `challenge`, the outstanding step (for ShiftIdentity v2, the
  /// `AuthenticationStep` name such as `PasswordChange`). Otherwise `null`.
  final String? step;

  @override
  bool operator ==(Object other) =>
      other is SignInRequired && other.reason == reason && other.step == step;

  @override
  int get hashCode => Object.hash(reason, step);

  @override
  String toString() => step == null
      ? 'SignInRequired($reason)'
      : 'SignInRequired($reason, step: $step)';
}

/// The issuer could not be reached and there is no usable access token. The
/// session is kept. The client does not call the issuer again until
/// [retryAfter] has passed.
final class TemporarilyUnavailable extends TokenOutcome {
  /// Creates the outcome.
  const TemporarilyUnavailable({required this.retryAfter});

  /// How long until the client calls the issuer again.
  final Duration retryAfter;

  @override
  bool operator ==(Object other) =>
      other is TemporarilyUnavailable && other.retryAfter == retryAfter;

  @override
  int get hashCode => retryAfter.hashCode;

  @override
  String toString() => 'TemporarilyUnavailable(retryAfter: $retryAfter)';
}

/// The reasons for [SignInRequired] that the client itself gives. An issuer
/// adapter adds its own.
abstract final class SignInReasons {
  /// The store holds no session.
  static const String noSession = 'noSession';

  /// The stored session has no refresh token, and its access token has expired.
  static const String noRefreshToken = 'noRefreshToken';

  /// The refresh token has expired by this device's clock. The store is kept.
  static const String refreshTokenExpired = 'refreshTokenExpired';

  /// `TokenClient.clear` was called while the caller was waiting.
  static const String cleared = 'cleared';

  /// The issuer refused the session without giving a reason.
  static const String rejected = 'rejected';
}
