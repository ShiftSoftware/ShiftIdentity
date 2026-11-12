import 'dart:async';
import 'dart:convert';

import 'package:fake_async/fake_async.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:shift_identity_token_client/shift_identity_token_client.dart';
import 'package:test/test.dart';

import 'support.dart';

void main() {
  group('HttpTokenTransport', () {
    test(
        'sends the request and returns any status with the body as UTF-8 and the headers',
        () async {
      late http.Request seen;
      final transport = HttpTokenTransport(MockClient((request) async {
        seen = request;
        return http.Response.bytes(utf8.encode('{"name":"Ərbil"}'), 400,
            headers: {'content-type': 'application/json', 'Retry-After': '3'});
      }));
      final response = await transport.send(TransportRequest(
        method: 'POST',
        url: Uri.parse('https://identity.example.test/api/identity/v2/refresh'),
        headers: const {'Content-Type': 'application/json'},
        body: '{"refreshToken":"r"}',
        timeout: const Duration(seconds: 5),
      ));
      expect(seen.method, 'POST');
      expect(seen.url.path, '/api/identity/v2/refresh');
      expect(seen.headers['Content-Type'], startsWith('application/json'));
      expect(seen.body, '{"refreshToken":"r"}');
      expect(response.statusCode, 400);
      expect(response.body, '{"name":"Ərbil"}');
      expect(response.header('retry-after'), '3');
      expect(response.header('RETRY-AFTER'), '3');
    });

    test('a client failure becomes a TransportException', () async {
      final transport = HttpTokenTransport(
          MockClient((request) async => throw http.ClientException('offline')));
      await expectLater(
        transport.send(
            TransportRequest(method: 'GET', url: Uri.parse('https://x.test/'))),
        throwsA(isA<TransportException>()
            .having((e) => e.timedOut, 'timedOut', false)),
      );
    });

    test('a slow answer becomes a timed-out TransportException', () {
      fakeAsync((async) {
        final transport = HttpTokenTransport(MockClient((request) async {
          await Future<void>.delayed(const Duration(seconds: 10));
          return http.Response('', 200);
        }));
        Object? error;
        transport
            .send(TransportRequest(
                method: 'GET',
                url: Uri.parse('https://x.test/'),
                timeout: const Duration(seconds: 3)))
            .then<void>((_) {}, onError: (Object e) {
          error = e;
        });
        async.elapse(const Duration(seconds: 3));
        expect(
            error,
            isA<TransportException>()
                .having((e) => e.timedOut, 'timedOut', true));
        async.elapse(const Duration(seconds: 10));
      });
    });

    test('works end to end with the v2 adapter', () async {
      final store = InMemoryCredentialStore(session(accessLeft: Duration.zero));
      final client = TokenClient(
        store: store,
        issuer: ShiftIdentityV2Issuer(
            baseUrl: 'https://identity.example.test', now: () => t0),
        transport: HttpTokenTransport(MockClient((request) async {
          expect(request.headers.containsKey('Authorization'), isFalse);
          return http.Response(
            jsonEncode({
              'kind': 'session',
              'Session': {
                'Token': 'access-2',
                'TokenLifeTimeInSeconds': 900,
                'RefreshToken': 'refresh-2',
                'RefreshTokenLifeTimeInSeconds': 3600,
                'Flow': 0,
                'UserData': null,
              },
            }),
            200,
          );
        })),
        now: () => t0,
      );
      expect((await client.acquireToken() as TokenAvailable).token, 'access-2');
      expect(store.session!.refreshToken, 'refresh-2');
    });
  });

  group('TokenHttpClient', () {
    late FakeStore store;
    late FakeIssuer issuer;
    late TokenClient tokens;
    late List<http.Request> sent;
    late MockClient inner;

    setUp(() {
      store = FakeStore(session());
      issuer = FakeIssuer();
      tokens = TokenClient(
          store: store,
          issuer: issuer,
          now: () => t0,
          transport: _NoTransport());
      sent = [];
      inner = MockClient((request) async {
        sent.add(request);
        return http.Response(
            'ok', request.url.path.endsWith('/denied') ? 401 : 200);
      });
    });

    TokenHttpClient api(
            {Iterable<Uri>? bases, bool Function(Uri)? isManaged}) =>
        TokenHttpClient(
          tokens: tokens,
          apiBaseUrls: isManaged == null
              ? (bases ?? [Uri.parse('https://api.example.test/v1/')])
              : null,
          isManaged: isManaged,
          inner: inner,
        );

    test('attaches the token to managed requests only', () async {
      final client = api();
      await client.get(Uri.parse('https://api.example.test/v1/orders?page=2'));
      await client.get(Uri.parse('https://api.example.test/v1'));
      await client.get(Uri.parse('https://api.example.test/v10/orders'));
      await client.get(Uri.parse('https://api.example.test/other'));
      await client.get(Uri.parse('http://api.example.test/v1/orders'));
      await client.get(Uri.parse('https://api.example.test:8443/v1/orders'));
      await client.get(Uri.parse('https://cdn.example.test/v1/logo.png'));
      expect(sent.map((r) => r.headers['Authorization']), [
        'Bearer access-1',
        'Bearer access-1',
        null,
        null,
        null,
        null,
        null,
      ]);
    });

    test(
        'a base URL without a path manages the whole host, matched without regard to case',
        () async {
      final client = api(bases: [Uri.parse('https://API.example.test')]);
      await client.get(Uri.parse('https://api.example.test/anything'));
      expect(sent.single.headers['Authorization'], 'Bearer access-1');
      expect(client.manages(Uri.parse('https://api.example.test')), isTrue);
    });

    test('isManaged decides instead of base URLs', () async {
      final client = api(isManaged: (url) => url.path.startsWith('/secure'));
      await client.get(Uri.parse('https://any.test/secure/x'));
      await client.get(Uri.parse('https://any.test/public/x'));
      expect(sent.map((r) => r.headers['Authorization']),
          ['Bearer access-1', null]);
    });

    test('a request that already has an Authorization header is sent as it is',
        () async {
      await api().get(Uri.parse('https://api.example.test/v1/x'),
          headers: {'authorization': 'Basic abc'});
      expect(sent.single.headers['authorization'], 'Basic abc');
      expect(issuer.calls, 0);
    });

    test('signInRequired is thrown and the request is not sent', () async {
      store.session = null;
      await expectLater(
        api().get(Uri.parse('https://api.example.test/v1/orders')),
        throwsA(isA<SignInRequiredException>()
            .having((e) => e.outcome.reason, 'reason', SignInReasons.noSession)
            .having((e) => e.url.path, 'url', '/v1/orders')),
      );
      expect(sent, isEmpty);
    });

    test('temporarilyUnavailable is thrown and the request is not sent',
        () async {
      store.session = session(accessLeft: Duration.zero);
      issuer.queued.add(const IssuerUnavailable());
      await expectLater(
        api().post(Uri.parse('https://api.example.test/v1/orders'), body: '{}'),
        throwsA(isA<TokenTemporarilyUnavailableException>().having(
            (e) => e.outcome.retryAfter,
            'retryAfter',
            const Duration(seconds: 2))),
      );
      expect(sent, isEmpty);
    });

    test('both exceptions can be caught as TokenAcquisitionException',
        () async {
      store.session = null;
      try {
        await api().get(Uri.parse('https://api.example.test/v1/orders'));
        fail('expected an exception');
      } on TokenAcquisitionException catch (e) {
        expect(e, isA<SignInRequiredException>());
      }
    });

    test(
        'a 401 from the API is returned as a response and does not clear the session',
        () async {
      final response =
          await api().get(Uri.parse('https://api.example.test/v1/denied'));
      expect(response.statusCode, 401);
      expect(store.session, isNotNull);
    });

    test('concurrent requests share one refresh', () async {
      store.session = session(accessLeft: Duration.zero);
      final client = api();
      final requests = Future.wait([
        for (var i = 0; i < 4; i++)
          client.get(Uri.parse('https://api.example.test/v1/$i'))
      ]);
      await settle();
      issuer.answer(IssuerSession(session(access: 'access-2')));
      await requests;
      expect(issuer.calls, 1);
      expect(sent.map((r) => r.headers['Authorization']),
          everyElement('Bearer access-2'));
    });

    test('refuses an invalid configuration', () {
      expect(() => TokenHttpClient(tokens: tokens), throwsArgumentError);
      expect(() => TokenHttpClient(tokens: tokens, apiBaseUrls: const []),
          throwsArgumentError);
      expect(
          () => TokenHttpClient(
              tokens: tokens, apiBaseUrls: [Uri.parse('/relative')]),
          throwsArgumentError);
      expect(
        () => TokenHttpClient(
            tokens: tokens,
            apiBaseUrls: [Uri.parse('https://a.test')],
            isManaged: (_) => true),
        throwsArgumentError,
      );
    });
  });
}

class _NoTransport implements TokenTransport {
  @override
  Future<TransportResponse> send(TransportRequest request) =>
      throw UnimplementedError();
}
