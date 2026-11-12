import 'dart:async';

import 'package:fake_async/fake_async.dart';
import 'package:shift_identity_token_client/shift_identity_token_client.dart';
import 'package:test/test.dart';

import 'support.dart';

void main() {
  late Clock clock;
  late FakeStore store;
  late FakeIssuer issuer;

  TokenClient client({
    Duration skew = const Duration(seconds: 60),
    Duration backoffInitial = const Duration(seconds: 2),
    Duration backoffMax = const Duration(seconds: 60),
    Duration timeout = const Duration(seconds: 30),
    TokenTransport? transport,
  }) =>
      TokenClient(
        store: store,
        issuer: issuer,
        skew: skew,
        backoffInitial: backoffInitial,
        backoffMax: backoffMax,
        timeout: timeout,
        transport: transport ?? _NoTransport(),
        now: clock.now,
      );

  SessionCredentials renewed([String access = 'access-2']) => session(
        access: access,
        refresh: 'refresh-2',
        at: clock.time,
      );

  setUp(() {
    clock = Clock();
    store = FakeStore();
    issuer = FakeIssuer();
  });

  group('when to refresh', () {
    test(
        'hands out the stored access token while more than the skew is left, without a refresh',
        () async {
      store.session = session(extra: {'userData': 'u'});
      final outcome = await client().acquireToken();
      expect(outcome,
          isA<TokenAvailable>().having((o) => o.token, 'token', 'access-1'));
      expect((outcome as TokenAvailable).expiresAt,
          t0.add(const Duration(minutes: 15)));
      expect(outcome.extra, {'userData': 'u'});
      expect(issuer.calls, 0);
    });

    test(
        'skew boundary: exactly the skew left needs no refresh, one millisecond less does',
        () async {
      store.session = session(accessLeft: const Duration(seconds: 60));
      expect(await client().acquireToken(), isA<TokenAvailable>());
      expect(issuer.calls, 0);

      store.session =
          session(accessLeft: const Duration(seconds: 59, milliseconds: 999));
      issuer.queued.add(IssuerSession(renewed()));
      final outcome = await client().acquireToken();
      expect((outcome as TokenAvailable).token, 'access-2');
      expect(issuer.calls, 1);
    });

    test('skew moves the refresh boundary', () async {
      store.session = session(accessLeft: const Duration(seconds: 20));
      expect(await client(skew: const Duration(seconds: 15)).acquireToken(),
          isA<TokenAvailable>());
      expect(issuer.calls, 0);
      issuer.queued.add(IssuerSession(renewed()));
      await client(skew: const Duration(seconds: 30)).acquireToken();
      expect(issuer.calls, 1);
    });

    test('a zero skew refreshes only once the access token has expired',
        () async {
      store.session = session(accessLeft: const Duration(milliseconds: 1));
      expect(await client(skew: Duration.zero).acquireToken(),
          isA<TokenAvailable>());
      store.session = session(accessLeft: Duration.zero);
      issuer.queued.add(IssuerSession(renewed()));
      expect(await client(skew: Duration.zero).acquireToken(),
          isA<TokenAvailable>());
      expect(issuer.calls, 1);
    });
  });

  group('refresh', () {
    test('concurrent callers share one refresh and one store write', () async {
      store.session = session(accessLeft: Duration.zero);
      final c = client();
      final outcomes =
          Future.wait([for (var i = 0; i < 5; i++) c.acquireToken()]);
      await settle();
      expect(issuer.calls, 1);
      issuer.answer(IssuerSession(renewed()));
      final results = await outcomes;
      expect(results.map((o) => (o as TokenAvailable).token),
          everyElement('access-2'));
      expect(issuer.calls, 1);
      expect(store.writes, 1);
      expect(store.session!.refreshToken, 'refresh-2');
    });

    test(
        'the issuer receives the stored session and a transport with the client timeout',
        () async {
      store.session = session(accessLeft: Duration.zero);
      final transport = _RecordingTransport();
      issuer.queued.add(IssuerSession(renewed()));
      await client(transport: transport, timeout: const Duration(seconds: 7))
          .acquireToken();
      expect(issuer.sessions.single.refreshToken, 'refresh-1');
      await issuer.transports.single.send(
          TransportRequest(method: 'POST', url: Uri.parse('https://x.test/')));
      await issuer.transports.single.send(TransportRequest(
          method: 'POST',
          url: Uri.parse('https://x.test/'),
          timeout: const Duration(seconds: 3)));
      expect(transport.requests.map((r) => r.timeout),
          [const Duration(seconds: 7), const Duration(seconds: 3)]);
    });

    test('the token is reported only after the new session is stored',
        () async {
      store.session = session(accessLeft: Duration.zero);
      store.writeGate = Completer<void>();
      issuer.queued.add(IssuerSession(renewed()));
      var done = false;
      final outcome = client().acquireToken().whenComplete(() => done = true);
      await settle();
      await settle();
      expect(store.writes, 1);
      expect(done, isFalse);
      store.writeGate!.complete();
      expect(await outcome, isA<TokenAvailable>());
      expect(store.log, ['read', 'write', 'written']);
    });

    test(
        'a rejected refresh clears the store and returns signInRequired with the reason and step',
        () async {
      store.session = session(accessLeft: Duration.zero);
      issuer.queued.add(
          const IssuerRejected(reason: 'challenge', step: 'PasswordChange'));
      expect(await client().acquireToken(),
          const SignInRequired(reason: 'challenge', step: 'PasswordChange'));
      expect(store.session, isNull);
    });

    test('a rejection without a reason still returns signInRequired', () async {
      store.session = session(accessLeft: Duration.zero);
      issuer.queued.add(const IssuerRejected(reason: ''));
      expect(await client().acquireToken(),
          const SignInRequired(reason: SignInReasons.rejected));
    });

    test(
        'an unavailable refresh keeps the store and returns temporarilyUnavailable when the access token has expired',
        () async {
      store.session = session(accessLeft: Duration.zero);
      issuer.queued.add(const IssuerUnavailable());
      expect(await client().acquireToken(),
          const TemporarilyUnavailable(retryAfter: Duration(seconds: 2)));
      expect(store.session, isNotNull);
      expect(store.clears, 0);
    });

    test(
        'an unavailable refresh still hands out the current access token while it has not expired',
        () async {
      store.session = session(accessLeft: const Duration(seconds: 30));
      issuer.queued.add(const IssuerUnavailable());
      final outcome = await client().acquireToken();
      expect((outcome as TokenAvailable).token, 'access-1');
      expect(issuer.calls, 1);
    });
  });

  group('back-off', () {
    test(
        'no issuer call during the delay, the delay doubles up to the maximum, and success resets it',
        () async {
      store.session = session(accessLeft: Duration.zero);
      final c = client(backoffMax: const Duration(seconds: 7));
      final waits = <Duration>[];
      for (var i = 0; i < 4; i++) {
        issuer.queued.add(const IssuerUnavailable());
        final outcome = await c.acquireToken() as TemporarilyUnavailable;
        waits.add(outcome.retryAfter);
        // During the delay, no call reaches the issuer.
        clock.advance(outcome.retryAfter - const Duration(milliseconds: 1));
        final waiting = await c.acquireToken() as TemporarilyUnavailable;
        expect(waiting.retryAfter, const Duration(milliseconds: 1));
        clock.advance(const Duration(milliseconds: 1));
      }
      expect(waits, const [
        Duration(seconds: 2),
        Duration(seconds: 4),
        Duration(seconds: 7),
        Duration(seconds: 7)
      ]);
      expect(issuer.calls, 4);

      issuer.queued.add(IssuerSession(session(
          access: 'access-2', accessLeft: Duration.zero, at: clock.time)));
      await c.acquireToken();
      issuer.queued.add(const IssuerUnavailable());
      expect(await c.acquireToken(),
          const TemporarilyUnavailable(retryAfter: Duration(seconds: 2)));
    });

    test('an issuer retryAfter longer than the back-off wins', () async {
      store.session = session(accessLeft: Duration.zero);
      issuer.queued
          .add(const IssuerUnavailable(retryAfter: Duration(seconds: 30)));
      final c = client();
      expect(await c.acquireToken(),
          const TemporarilyUnavailable(retryAfter: Duration(seconds: 30)));
      clock.advance(const Duration(seconds: 29));
      expect(await c.acquireToken(),
          const TemporarilyUnavailable(retryAfter: Duration(seconds: 1)));
      expect(issuer.calls, 1);
    });

    test('backoffInitial and backoffMax set the delays', () async {
      store.session = session(accessLeft: Duration.zero);
      final c = client(
          backoffInitial: const Duration(seconds: 5),
          backoffMax: const Duration(seconds: 12));
      final waits = <Duration>[];
      for (var i = 0; i < 3; i++) {
        issuer.queued
            .add(const IssuerUnavailable(retryAfter: Duration(seconds: 1)));
        final outcome = await c.acquireToken() as TemporarilyUnavailable;
        waits.add(outcome.retryAfter);
        clock.advance(outcome.retryAfter);
      }
      expect(waits, const [
        Duration(seconds: 5),
        Duration(seconds: 10),
        Duration(seconds: 12)
      ]);
    });

    test('a huge back-off does not overflow', () async {
      store.session = session(accessLeft: Duration.zero, refreshLeft: null);
      final c = client(
          backoffInitial: const Duration(days: 1),
          backoffMax: const Duration(days: 3650));
      for (var i = 0; i < 70; i++) {
        issuer.queued.add(const IssuerUnavailable());
        final outcome = await c.acquireToken() as TemporarilyUnavailable;
        expect(outcome.retryAfter.inDays, inInclusiveRange(1, 3650));
        clock.advance(outcome.retryAfter);
      }
    });
  });

  group('the stored session', () {
    test('no stored session returns signInRequired without calling the issuer',
        () async {
      expect(await client().acquireToken(),
          const SignInRequired(reason: SignInReasons.noSession));
      expect(issuer.calls, 0);
    });

    test('a stored value without tokens counts as no session', () async {
      store.session =
          const SessionCredentials(accessToken: '', refreshToken: '');
      expect(await client().acquireToken(),
          const SignInRequired(reason: SignInReasons.noSession));
    });

    test(
        'an expired refresh token returns signInRequired without calling the issuer, and the store is kept',
        () async {
      store.session =
          session(accessLeft: Duration.zero, refreshLeft: Duration.zero);
      expect(await client().acquireToken(),
          const SignInRequired(reason: SignInReasons.refreshTokenExpired));
      expect(issuer.calls, 0);
      expect(store.session, isNotNull);
    });

    test(
        'without a usable refresh token, the access token is handed out until it expires',
        () async {
      store.session =
          session(accessLeft: const Duration(seconds: 10), refresh: null);
      expect(await client().acquireToken(), isA<TokenAvailable>());
      store.session = session(accessLeft: Duration.zero, refresh: null);
      expect(await client().acquireToken(),
          const SignInRequired(reason: SignInReasons.noRefreshToken));
      store.session = session(
          accessLeft: const Duration(seconds: 10), refreshLeft: Duration.zero);
      expect(await client().acquireToken(), isA<TokenAvailable>());
      expect(issuer.calls, 0);
    });

    test('an unknown refresh expiry is left to the issuer', () async {
      store.session = session(accessLeft: Duration.zero, refreshLeft: null);
      issuer.queued.add(IssuerSession(renewed()));
      expect(await client().acquireToken(), isA<TokenAvailable>());
      expect(issuer.calls, 1);
    });

    test('a session with only a refresh token is refreshed', () async {
      store.session = session(access: null, accessLeft: null);
      issuer.queued.add(IssuerSession(renewed()));
      expect(
          (await client().acquireToken() as TokenAvailable).token, 'access-2');
    });
  });

  group('clear and replaceSession', () {
    test(
        'clear during a refresh: waiters get signInRequired, the late session is discarded, and the next call starts fresh',
        () async {
      store.session = session(accessLeft: Duration.zero);
      final c = client();
      final waiting = [c.acquireToken(), c.acquireToken()];
      await settle();
      await c.clear();
      expect(await Future.wait(waiting),
          everyElement(const SignInRequired(reason: SignInReasons.cleared)));

      issuer.answer(IssuerSession(renewed('late')));
      await settle();
      expect(store.session, isNull,
          reason: 'the late session must not be stored');
      expect(store.writes, 0);

      store.session = session(accessLeft: Duration.zero);
      final next = c.acquireToken();
      await settle();
      expect(issuer.calls, 2, reason: 'a new refresh starts');
      issuer.answer(IssuerSession(renewed('access-3')));
      expect((await next as TokenAvailable).token, 'access-3');
    });

    test(
        'a late rejection or failure after clear does not touch the store or the back-off',
        () async {
      store.session = session(accessLeft: Duration.zero);
      final c = client();
      unawaited(c.acquireToken());
      await settle();
      await c.clear();
      store.session = session(accessLeft: Duration.zero);
      issuer.answer(const IssuerRejected(reason: 'InvalidGrant'));
      await settle();
      expect(store.session, isNotNull);

      unawaited(c.acquireToken());
      await settle();
      await c.clear();
      store.session = session(accessLeft: Duration.zero);
      issuer.answer(const IssuerUnavailable(retryAfter: Duration(hours: 1)));
      await settle();
      issuer.queued.add(IssuerSession(renewed()));
      expect(await c.acquireToken(), isA<TokenAvailable>(),
          reason: 'no back-off from the discarded failure');
    });

    test('clear during the store read discards that read', () async {
      store.session = session();
      store.readGate = Completer<void>();
      final c = client();
      final outcome = c.acquireToken();
      await settle();
      await c.clear();
      store.readGate!.complete();
      expect(
          await outcome, const SignInRequired(reason: SignInReasons.cleared));
    });

    test('clear resets the back-off', () async {
      store.session = session(accessLeft: Duration.zero);
      final c = client();
      issuer.queued
          .add(const IssuerUnavailable(retryAfter: Duration(minutes: 5)));
      expect(await c.acquireToken(), isA<TemporarilyUnavailable>());
      await c.clear();
      store.session = session(accessLeft: Duration.zero);
      issuer.queued.add(IssuerSession(renewed()));
      expect(await c.acquireToken(), isA<TokenAvailable>());
    });

    test(
        'replaceSession stores the session, discards a running refresh, and answers waiters with the new session',
        () async {
      store.session = session(accessLeft: Duration.zero);
      final c = client();
      final waiting = c.acquireToken();
      await settle();
      await c
          .replaceSession(session(access: 'signed-in', refresh: 'refresh-new'));
      expect((await waiting as TokenAvailable).token, 'signed-in');
      issuer.answer(IssuerSession(renewed('late')));
      await settle();
      expect(store.session!.accessToken, 'signed-in');
      expect((await c.acquireToken() as TokenAvailable).token, 'signed-in');
    });

    test('replaceSession refuses a session without tokens', () async {
      expect(() => client().replaceSession(const SessionCredentials()),
          throwsArgumentError);
    });
  });

  group('programmer and platform errors', () {
    test('a store write failure rejects acquireToken', () async {
      store.session = session(accessLeft: Duration.zero);
      store.writeError = StateError('disk full');
      issuer.queued.add(IssuerSession(renewed()));
      await expectLater(
        client().acquireToken(),
        throwsA(isA<CredentialStoreException>()
            .having((e) => e.operation, 'operation', 'write')
            .having((e) => e.cause, 'cause', isA<StateError>())),
      );
    });

    test(
        'a store read failure rejects acquireToken, also when the store throws synchronously',
        () async {
      for (final sync in [false, true]) {
        store
          ..readError = StateError('locked')
          ..throwSynchronously = sync;
        await expectLater(
          client().acquireToken(),
          throwsA(isA<CredentialStoreException>()
              .having((e) => e.operation, 'operation', 'read')),
        );
      }
    });

    test('a store clear failure after a rejection rejects acquireToken',
        () async {
      store.session = session(accessLeft: Duration.zero);
      store.clearError = StateError('locked');
      issuer.queued.add(const IssuerRejected(reason: 'InvalidGrant'));
      await expectLater(
        client().acquireToken(),
        throwsA(isA<CredentialStoreException>()
            .having((e) => e.operation, 'operation', 'clear')),
      );
    });

    test('a store clear failure rejects clear and the waiters', () async {
      store.session = session(accessLeft: Duration.zero);
      final c = client();
      final waiting = c.acquireToken();
      await settle();
      store.clearError = StateError('locked');
      await expectLater(c.clear(), throwsA(isA<CredentialStoreException>()));
      await expectLater(waiting, throwsA(isA<CredentialStoreException>()));
    });

    test('after a failure the next call starts a new acquisition', () async {
      store.readError = StateError('locked');
      final c = client();
      await expectLater(
          c.acquireToken(), throwsA(isA<CredentialStoreException>()));
      store.readError = null;
      expect(await c.acquireToken(),
          const SignInRequired(reason: SignInReasons.noSession));
    });

    test('refuses an issuer with an unknown contractVersion or an empty id',
        () {
      issuer = FakeIssuer(contractVersion: 2);
      expect(
          client,
          throwsA(isA<ArgumentError>()
              .having((e) => '$e', 'message', contains('version 2'))));
      issuer = FakeIssuer(id: '');
      expect(client, throwsArgumentError);
    });

    test('refuses an invalid configuration', () {
      expect(
          () => client(skew: const Duration(seconds: -1)), throwsArgumentError);
      expect(() => client(backoffInitial: Duration.zero), throwsArgumentError);
      expect(() => client(backoffMax: Duration.zero), throwsArgumentError);
      expect(
          () => client(
              backoffInitial: const Duration(seconds: 5),
              backoffMax: const Duration(seconds: 4)),
          throwsArgumentError);
      expect(() => client(timeout: Duration.zero), throwsArgumentError);
    });

    test('an adapter that throws rejects acquireToken', () async {
      store.session = session(accessLeft: Duration.zero);
      issuer.throwError = StateError('adapter bug');
      await expectLater(client().acquireToken(), throwsA(isA<StateError>()));
    });

    test('an adapter session that is incomplete rejects acquireToken',
        () async {
      store.session = session(accessLeft: Duration.zero);
      issuer.queued.add(const IssuerSession(
          SessionCredentials(accessToken: 'a', refreshToken: 'r')));
      await expectLater(
        client().acquireToken(),
        throwsA(isA<StateError>()
            .having((e) => e.message, 'message', contains('"fake"'))),
      );
      expect(store.writes, 0);
    });

    test(
        'an adapter that never answers is treated as unavailable after the timeout',
        () {
      fakeAsync((async) {
        store.session = session(accessLeft: Duration.zero);
        TokenOutcome? outcome;
        client(timeout: const Duration(seconds: 5))
            .acquireToken()
            .then((o) => outcome = o);
        async.elapse(const Duration(seconds: 5, milliseconds: 999));
        expect(outcome, isNull);
        async.elapse(const Duration(milliseconds: 1));
        expect(outcome,
            const TemporarilyUnavailable(retryAfter: Duration(seconds: 2)));
        // An answer after the watchdog is ignored.
        issuer.answer(IssuerSession(renewed()));
        async.flushMicrotasks();
        expect(store.writes, 0);
      });
    });
  });

  test('close closes only the default transport', () {
    TokenClient(store: store, issuer: issuer).close();
    client().close();
  });
}

class _NoTransport implements TokenTransport {
  @override
  Future<TransportResponse> send(TransportRequest request) =>
      throw UnimplementedError();
}

class _RecordingTransport implements TokenTransport {
  final List<TransportRequest> requests = <TransportRequest>[];

  @override
  Future<TransportResponse> send(TransportRequest request) async {
    requests.add(request);
    return TransportResponse(statusCode: 200);
  }
}
