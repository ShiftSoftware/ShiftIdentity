import 'dart:async';

import 'credential_store.dart';
import 'session_credentials.dart';
import 'token_issuer.dart';
import 'token_outcome.dart';
import 'transport.dart';

/// Holds a session and hands out a usable access token.
///
/// It refreshes the session when the access token is close to expiry, runs one
/// refresh at a time, and tells "sign in again" apart from "temporarily
/// unavailable". It never navigates and never runs a background timer: call
/// [acquireToken] before each request, or let `TokenHttpClient` do it.
///
/// ```dart
/// final client = TokenClient(
///   store: InMemoryCredentialStore(),
///   issuer: ShiftIdentityV2Issuer(baseUrl: 'https://identity.example.com'),
/// );
/// ```
///
/// Create one client per signed-in session and issuer, and share it. Calls
/// that arrive while a refresh runs wait for that refresh instead of starting
/// another.
class TokenClient {
  /// Creates the client.
  ///
  /// * [store] keeps the session. The client reads it on every call and keeps
  ///   no copy in memory, so it picks up a session that a sign-in wrote.
  /// * [issuer] renews the session, for example `ShiftIdentityV2Issuer`.
  /// * [skew]: refresh when less than this is left (default 60 s). Keep it
  ///   below the access token lifetime, or every call refreshes.
  /// * [backoffInitial]: the wait after the first failed refresh (default
  ///   2 s). It doubles after each failure in a row, up to [backoffMax]
  ///   (default 60 s). An issuer's `retryAfter` can ask for longer.
  /// * [timeout]: the refresh request timeout (default 30 s). An adapter that
  ///   never answers is treated as unavailable one second after it.
  /// * [transport] sends the adapter's requests. Default: [HttpTokenTransport].
  /// * [now] returns the current time. For tests.
  ///
  /// Throws an [ArgumentError] for an invalid configuration, including an
  /// issuer that implements another adapter [TokenIssuer.contractVersion].
  TokenClient({
    required CredentialStore store,
    required TokenIssuer issuer,
    Duration skew = const Duration(seconds: 60),
    Duration backoffInitial = const Duration(seconds: 2),
    Duration backoffMax = const Duration(seconds: 60),
    Duration timeout = const Duration(seconds: 30),
    TokenTransport? transport,
    DateTime Function()? now,
  })  : _store = store,
        _issuer = _validIssuer(issuer),
        _skew = _notNegative(skew, 'skew'),
        _backoffInitial = _positive(backoffInitial, 'backoffInitial'),
        _backoffMax = _validBackoffMax(backoffMax, backoffInitial),
        _timeout = _positive(timeout, 'timeout'),
        _now = now ?? DateTime.now,
        _ownedTransport = transport == null ? HttpTokenTransport() : null,
        _givenTransport = transport;

  /// How much longer than the request timeout the client waits for an adapter
  /// that never answers. A transport that honours the timeout answers first,
  /// so the adapter can map the failure itself.
  static const Duration watchdogGrace = Duration(seconds: 1);

  final CredentialStore _store;
  final TokenIssuer _issuer;
  final Duration _skew;
  final Duration _backoffInitial;
  final Duration _backoffMax;
  final Duration _timeout;
  final DateTime Function() _now;
  final HttpTokenTransport? _ownedTransport;
  final TokenTransport? _givenTransport;

  late final TokenTransport _transport =
      _BoundTransport(_givenTransport ?? _ownedTransport!, _timeout);

  // clear() and replaceSession() increase the generation. Work that started
  // under an older generation is discarded when it ends.
  int _generation = 0;

  // The one running acquisition. Callers that arrive while it runs wait for
  // its outcome.
  _Operation? _operation;

  int _failures = 0;
  DateTime? _retryAt;

  /// The issuer adapter this client uses.
  TokenIssuer get issuer => _issuer;

  /// The store this client uses.
  CredentialStore get store => _store;

  /// Returns a usable access token, or says why there is none.
  ///
  /// Completes with exactly one [TokenOutcome]:
  /// * [TokenAvailable]: the access token has at least the skew left, or was
  ///   just refreshed and stored.
  /// * [SignInRequired]: no session, no usable refresh token, the issuer
  ///   refused the session (the store is then cleared), or [clear] was called.
  /// * [TemporarilyUnavailable]: the issuer could not be reached and there is
  ///   no usable access token. The session is kept.
  ///
  /// Authentication conditions are never errors. It throws only for
  /// programmer and platform errors: a [CredentialStoreException], an error
  /// thrown by the issuer adapter, or a [StateError] for an adapter that
  /// returned an incomplete session.
  Future<TokenOutcome> acquireToken() {
    final running = _operation;
    if (running != null) return running.join();
    final operation = _Operation(_generation);
    _operation = operation;
    final outcome = operation.join();
    unawaited(_run(operation));
    return outcome;
  }

  /// Removes the session from the store (sign-out) and resets the back-off.
  ///
  /// Callers still waiting for [acquireToken] receive [SignInRequired] with
  /// the reason [SignInReasons.cleared], and a refresh that is still running
  /// is discarded when it ends. Throws a [CredentialStoreException] when the
  /// store fails.
  Future<void> clear() async {
    final detached = _restart();
    try {
      await _store.clear();
    } catch (error, stack) {
      final failure = CredentialStoreException('clear', error, stack);
      for (final waiter in detached) {
        waiter.completeError(failure, stack);
      }
      Error.throwWithStackTrace(failure, stack);
    }
    for (final waiter in detached) {
      waiter.complete(const SignInRequired(reason: SignInReasons.cleared));
    }
  }

  /// Stores a new session, for example right after a sign-in, and resets the
  /// back-off.
  ///
  /// A refresh that is still running is discarded when it ends, so it cannot
  /// overwrite [session]. Callers still waiting for [acquireToken] receive the
  /// outcome for [session]. Throws an [ArgumentError] for a session without
  /// any token, and a [CredentialStoreException] when the store fails.
  Future<void> replaceSession(SessionCredentials session) async {
    if (!session.isReadable) {
      throw ArgumentError.value(
          session, 'session', 'needs an access token or a refresh token');
    }
    final detached = _restart();
    try {
      await _store.write(session);
    } catch (error, stack) {
      final failure = CredentialStoreException('write', error, stack);
      for (final waiter in detached) {
        waiter.completeError(failure, stack);
      }
      Error.throwWithStackTrace(failure, stack);
    }
    if (detached.isEmpty) return;
    final next = acquireToken();
    for (final waiter in detached) {
      waiter.complete(next);
    }
  }

  /// Closes the HTTP client of the default transport. A transport passed to
  /// the constructor is left open.
  void close() => _ownedTransport?.close();

  List<Completer<TokenOutcome>> _restart() {
    _generation += 1;
    _resetBackoff();
    final operation = _operation;
    _operation = null;
    return operation == null ? const [] : operation.takeWaiters();
  }

  bool _isStale(_Operation operation) => operation.generation != _generation;

  void _resetBackoff() {
    _failures = 0;
    _retryAt = null;
  }

  void _finish(_Operation operation, TokenOutcome outcome) {
    if (identical(_operation, operation)) _operation = null;
    for (final waiter in operation.takeWaiters()) {
      waiter.complete(outcome);
    }
  }

  void _fail(_Operation operation, Object error, StackTrace stack) {
    if (identical(_operation, operation)) _operation = null;
    for (final waiter in operation.takeWaiters()) {
      waiter.completeError(error, stack);
    }
  }

  Future<void> _run(_Operation operation) async {
    try {
      await _acquire(operation);
    } catch (error, stack) {
      if (!_isStale(operation)) _fail(operation, error, stack);
    }
  }

  Future<void> _acquire(_Operation operation) async {
    final SessionCredentials? stored;
    try {
      stored = await _store.read();
    } catch (error, stack) {
      if (!_isStale(operation)) {
        _fail(operation, CredentialStoreException('read', error, stack), stack);
      }
      return;
    }
    if (_isStale(operation)) return;

    if (stored == null || !stored.isReadable) {
      _finish(operation, const SignInRequired(reason: SignInReasons.noSession));
      return;
    }
    final session = stored;
    final now = _now();
    final left = _accessLeft(session, now);

    // Refresh only when less than the skew is left. Exactly the skew left is
    // still enough.
    if (left > Duration.zero && left >= _skew) {
      _finish(operation, _available(session));
      return;
    }

    // Without a refresh, the current access token is handed out while it lasts.
    final hasRefreshToken = (session.refreshToken ?? '').isNotEmpty;
    if (!hasRefreshToken || _refreshExpired(session, now)) {
      _finish(
        operation,
        left > Duration.zero
            ? _available(session)
            : SignInRequired(
                reason: hasRefreshToken
                    ? SignInReasons.refreshTokenExpired
                    : SignInReasons.noRefreshToken,
              ),
      );
      return;
    }

    // The issuer was unavailable recently. Wait for the retry delay before
    // asking it again.
    final retryAt = _retryAt;
    if (retryAt != null && now.isBefore(retryAt)) {
      _finish(
        operation,
        left > Duration.zero
            ? _available(session)
            : TemporarilyUnavailable(retryAfter: retryAt.difference(now)),
      );
      return;
    }

    final IssuerResult result;
    try {
      // An adapter or transport that never answers would block every later
      // caller. Treat it as unavailable.
      result = await Future<IssuerResult>.sync(
              () => _issuer.refresh(session, _transport))
          .timeout(_timeout + watchdogGrace,
              onTimeout: () => const IssuerUnavailable());
    } catch (error, stack) {
      if (!_isStale(operation)) _fail(operation, error, stack);
      return;
    }
    if (_isStale(operation)) return;
    await _apply(operation, session, result);
  }

  Future<void> _apply(_Operation operation, SessionCredentials current,
      IssuerResult result) async {
    switch (result) {
      case IssuerSession(:final session):
        if (!session.isComplete) {
          _fail(
            operation,
            StateError(
                'issuer "${_issuer.id}" returned a session without accessToken, '
                'accessExpiresAt and refreshToken.'),
            StackTrace.current,
          );
          return;
        }
        _resetBackoff();
        // Success is reported only after the new session is stored.
        try {
          await _store.write(session);
        } catch (error, stack) {
          if (!_isStale(operation)) {
            _fail(operation, CredentialStoreException('write', error, stack),
                stack);
          }
          return;
        }
        if (!_isStale(operation)) _finish(operation, _available(session));

      case IssuerRejected(:final reason, :final step):
        _resetBackoff();
        try {
          await _store.clear();
        } catch (error, stack) {
          if (!_isStale(operation)) {
            _fail(operation, CredentialStoreException('clear', error, stack),
                stack);
          }
          return;
        }
        if (!_isStale(operation)) {
          _finish(
              operation,
              SignInRequired(
                  reason: reason.isEmpty ? SignInReasons.rejected : reason,
                  step: step));
        }

      case IssuerUnavailable(:final retryAfter):
        // The session stays in the store. Wait longer after each failure in a
        // row, up to the maximum, and never less than the issuer asked for.
        final now = _now();
        _failures += 1;
        var delay = _backoffInitial;
        for (var i = 1; i < _failures && delay < _backoffMax; i++) {
          delay *= 2;
        }
        if (delay > _backoffMax) delay = _backoffMax;
        if (retryAfter != null && retryAfter > delay) delay = retryAfter;
        _retryAt = now.add(delay);
        _finish(
          operation,
          _accessLeft(current, now) > Duration.zero
              ? _available(current)
              : TemporarilyUnavailable(retryAfter: delay),
        );
    }
  }
}

final class _Operation {
  _Operation(this.generation);

  final int generation;
  List<Completer<TokenOutcome>> _waiters = <Completer<TokenOutcome>>[];

  Future<TokenOutcome> join() {
    final waiter = Completer<TokenOutcome>();
    _waiters.add(waiter);
    return waiter.future;
  }

  List<Completer<TokenOutcome>> takeWaiters() {
    final waiters = _waiters;
    _waiters = <Completer<TokenOutcome>>[];
    return waiters;
  }
}

// Gives the adapter a transport that applies the client's timeout.
final class _BoundTransport implements TokenTransport {
  _BoundTransport(this._inner, this._timeout);

  final TokenTransport _inner;
  final Duration _timeout;

  @override
  Future<TransportResponse> send(TransportRequest request) => _inner
      .send(request.timeout == null ? request.withTimeout(_timeout) : request);
}

TokenAvailable _available(SessionCredentials session) => TokenAvailable(
    token: session.accessToken!,
    expiresAt: session.accessExpiresAt!,
    extra: session.extra);

// How much access is left, or zero when there is no usable access token.
Duration _accessLeft(SessionCredentials session, DateTime now) {
  final token = session.accessToken;
  final expiresAt = session.accessExpiresAt;
  if (token == null || token.isEmpty || expiresAt == null) return Duration.zero;
  final left = expiresAt.difference(now);
  return left.isNegative ? Duration.zero : left;
}

// An unknown refresh expiry is not treated as expired. The issuer decides.
bool _refreshExpired(SessionCredentials session, DateTime now) {
  final expiresAt = session.refreshExpiresAt;
  return expiresAt != null && !now.isBefore(expiresAt);
}

TokenIssuer _validIssuer(TokenIssuer issuer) {
  if (issuer.id.isEmpty) {
    throw ArgumentError.value(issuer, 'issuer', 'id must not be empty');
  }
  if (issuer.contractVersion != issuerContractVersion) {
    throw ArgumentError.value(
      issuer.contractVersion,
      'issuer.contractVersion',
      'issuer "${issuer.id}" implements adapter contract version ${issuer.contractVersion}, '
          'but this client supports only version $issuerContractVersion',
    );
  }
  return issuer;
}

Duration _notNegative(Duration value, String name) {
  if (value.isNegative) {
    throw ArgumentError.value(value, name, 'must not be negative');
  }
  return value;
}

Duration _positive(Duration value, String name) {
  if (value <= Duration.zero) {
    throw ArgumentError.value(value, name, 'must be positive');
  }
  return value;
}

Duration _validBackoffMax(Duration max, Duration initial) {
  _positive(max, 'backoffMax');
  if (max < initial) {
    throw ArgumentError.value(
        max, 'backoffMax', 'must not be less than backoffInitial');
  }
  return max;
}
