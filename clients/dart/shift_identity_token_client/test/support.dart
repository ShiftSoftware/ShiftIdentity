// Fakes shared by the tests: a store, an issuer and a clock that the test controls.
import 'dart:async';

import 'package:shift_identity_token_client/shift_identity_token_client.dart';

final DateTime t0 = DateTime.utc(2026, 10, 8, 10);

SessionCredentials session({
  String? access = 'access-1',
  Duration? accessLeft = const Duration(minutes: 15),
  String? refresh = 'refresh-1',
  Duration? refreshLeft = const Duration(days: 30),
  DateTime? at,
  Object? extra,
}) {
  final now = at ?? t0;
  return SessionCredentials(
    accessToken: access,
    accessExpiresAt: accessLeft == null ? null : now.add(accessLeft),
    refreshToken: refresh,
    refreshExpiresAt: refreshLeft == null ? null : now.add(refreshLeft),
    extra: extra,
  );
}

class Clock {
  Clock([DateTime? start]) : time = start ?? t0;
  DateTime time;
  DateTime now() => time;
  void advance(Duration by) => time = time.add(by);
}

class FakeStore implements CredentialStore {
  FakeStore([this.session]);

  SessionCredentials? session;
  int reads = 0;
  int writes = 0;
  int clears = 0;
  Object? readError;
  Object? writeError;
  Object? clearError;
  bool throwSynchronously = false;

  /// When set, read waits for it.
  Completer<void>? readGate;

  /// When set, write waits for it before storing.
  Completer<void>? writeGate;

  final List<String> log = <String>[];

  Future<T> _fail<T>(Object error) {
    if (throwSynchronously) throw error;
    return Future<T>.error(error);
  }

  @override
  Future<SessionCredentials?> read() async {
    reads++;
    log.add('read');
    if (readError != null) return _fail(readError!);
    final gate = readGate;
    if (gate != null) await gate.future;
    return session;
  }

  @override
  Future<void> write(SessionCredentials value) async {
    writes++;
    log.add('write');
    if (writeError != null) return _fail(writeError!);
    final gate = writeGate;
    if (gate != null) await gate.future;
    session = value;
    log.add('written');
  }

  @override
  Future<void> clear() {
    clears++;
    log.add('clear');
    if (clearError != null) return _fail(clearError!);
    session = null;
    return Future<void>.value();
  }
}

/// An issuer whose answers the test gives one by one.
class FakeIssuer implements TokenIssuer {
  FakeIssuer({this.id = 'fake', this.contractVersion = 1});

  @override
  final String id;

  @override
  final int contractVersion;

  final List<SessionCredentials> sessions = <SessionCredentials>[];
  final List<TokenTransport> transports = <TokenTransport>[];
  final List<Completer<IssuerResult>> pending = <Completer<IssuerResult>>[];

  /// Answers that are given at once, in order. When empty, refresh waits for [answer].
  final List<IssuerResult> queued = <IssuerResult>[];

  Object? throwError;

  int get calls => sessions.length;

  @override
  Future<IssuerResult> refresh(
      SessionCredentials session, TokenTransport transport) {
    sessions.add(session);
    transports.add(transport);
    if (throwError != null) throw throwError!;
    if (queued.isNotEmpty) {
      return Future<IssuerResult>.value(queued.removeAt(0));
    }
    final completer = Completer<IssuerResult>();
    pending.add(completer);
    return completer.future;
  }

  void answer(IssuerResult result) => pending.removeAt(0).complete(result);
}

/// Lets queued microtasks and zero timers run.
Future<void> settle() => Future<void>.delayed(Duration.zero);
