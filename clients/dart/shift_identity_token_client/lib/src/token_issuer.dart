import 'session_credentials.dart';
import 'transport.dart';

/// The version of the issuer adapter contract that this client implements.
/// An adapter declares the version it was written for in
/// [TokenIssuer.contractVersion], and the client refuses any other.
const int issuerContractVersion = 1;

/// An issuer adapter: it renews a session with one particular token issuer.
///
/// The adapter owns the request shape and the mapping of the answer. The
/// client owns everything else: when to refresh, single flight, back-off,
/// storage, and the outcome. Adapters for other issuers can ship from their
/// own packages.
///
/// Contract version 1:
/// * [refresh] sends its requests through the given [TokenTransport], and
///   completes with an [IssuerResult]. It must not throw for a network
///   failure or a refusal; map those to [IssuerUnavailable] or
///   [IssuerRejected]. Throwing means a bug in the adapter, and the client
///   passes the error on to its caller.
/// * Only a definite answer from the issuer may be [IssuerRejected]. Anything
///   unclear (5xx, a proxy page, an unknown body) is [IssuerUnavailable].
abstract interface class TokenIssuer {
  /// A stable identifier, for example `shift-identity-v2`.
  String get id;

  /// The adapter contract version this adapter implements. Must be
  /// [issuerContractVersion].
  int get contractVersion;

  /// Exchanges the refresh token of [session] for a new session.
  Future<IssuerResult> refresh(
      SessionCredentials session, TokenTransport transport);
}

/// What [TokenIssuer.refresh] completes with.
sealed class IssuerResult {
  const IssuerResult();
}

/// The issuer renewed the session. [session] must be complete
/// ([SessionCredentials.isComplete]).
final class IssuerSession extends IssuerResult {
  /// Creates the result.
  const IssuerSession(this.session);

  /// The new session.
  final SessionCredentials session;
}

/// The issuer refused: the session cannot be renewed, so the user must sign
/// in again. The client clears the store.
final class IssuerRejected extends IssuerResult {
  /// Creates the result.
  const IssuerRejected({required this.reason, this.step});

  /// Why, in the issuer's words (for example `InvalidGrant`).
  final String reason;

  /// An outstanding step the user must complete, if the issuer named one.
  final String? step;

  @override
  bool operator ==(Object other) =>
      other is IssuerRejected && other.reason == reason && other.step == step;

  @override
  int get hashCode => Object.hash(reason, step);

  @override
  String toString() =>
      'IssuerRejected($reason${step == null ? '' : ', step: $step'})';
}

/// The issuer could not be reached or could not answer now. The client keeps
/// the session and backs off.
final class IssuerUnavailable extends IssuerResult {
  /// Creates the result.
  const IssuerUnavailable({this.retryAfter});

  /// How long the issuer asked to wait (for example from `Retry-After`).
  /// The client waits at least this long, and longer if its back-off says so.
  final Duration? retryAfter;

  @override
  bool operator ==(Object other) =>
      other is IssuerUnavailable && other.retryAfter == retryAfter;

  @override
  int get hashCode => retryAfter.hashCode;

  @override
  String toString() => 'IssuerUnavailable(retryAfter: $retryAfter)';
}
