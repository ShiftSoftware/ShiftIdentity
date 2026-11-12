// The client over real sockets: a local server that answers POST /api/identity/v2/refresh the way a framework host
// does (PascalCase), plus the failure cases a network adds.
@TestOn('vm')
library;

import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:shift_identity_token_client/shift_identity_token_client.dart';
import 'package:test/test.dart';

void main() {
  late HttpServer server;
  late String baseUrl;
  // What the server does with the next refresh: 'session', 'refused', 'unavailable', 'hang'.
  var mode = 'session';
  var refreshes = 0;
  final apiAuthorizations = <String?>[];

  setUp(() async {
    mode = 'session';
    refreshes = 0;
    apiAuthorizations.clear();
    server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    baseUrl = 'http://127.0.0.1:${server.port}';
    server.listen((request) async {
      final response = request.response;
      if (request.uri.path == '/api/identity/v2/refresh' &&
          request.method == 'POST') {
        refreshes++;
        final body = jsonDecode(await utf8.decoder.bind(request).join())
            as Map<String, dynamic>;
        response.headers.contentType = ContentType.json;
        switch (mode) {
          case 'session':
            response.write(jsonEncode({
              'kind': 'session',
              'Session': {
                'Token': 'access-$refreshes',
                'TokenLifeTimeInSeconds': 90,
                'RefreshToken':
                    'refresh-$refreshes-from-${body['refreshToken']}',
                'RefreshTokenLifeTimeInSeconds': 3600,
                'Flow': 0,
                'UserData': {'Username': 'board-screen'},
              },
            }));
          case 'refused':
            response.statusCode = 400;
            response.write(jsonEncode(
                {'kind': 'refused', 'Code': 2, 'PasswordFailure': null}));
          case 'unavailable':
            response.statusCode = 503;
            response.headers.set('Retry-After', '7');
            response.write(jsonEncode(
                {'kind': 'refused', 'Code': 8, 'PasswordFailure': null}));
          case 'hang':
            await Future<void>.delayed(const Duration(seconds: 3));
        }
      } else if (request.uri.path.startsWith('/api/')) {
        apiAuthorizations.add(request.headers.value('authorization'));
        response.write('ok');
      } else {
        response.statusCode = 404;
      }
      await response.close();
    });
  });

  tearDown(() => server.close(force: true));

  TokenClient client(InMemoryCredentialStore store,
          {Duration timeout = const Duration(seconds: 30)}) =>
      TokenClient(
          store: store,
          issuer: ShiftIdentityV2Issuer(baseUrl: baseUrl),
          timeout: timeout);

  SessionCredentials expired() => SessionCredentials(
        accessToken: 'access-0',
        accessExpiresAt:
            DateTime.now().toUtc().subtract(const Duration(seconds: 1)),
        refreshToken: 'refresh-0',
      );

  test('refreshes over HTTP, stores the new session, and shares one refresh',
      () async {
    final store = InMemoryCredentialStore(expired());
    final tokens = client(store);
    final outcomes =
        await Future.wait([for (var i = 0; i < 5; i++) tokens.acquireToken()]);
    expect(outcomes.map((o) => (o as TokenAvailable).token),
        everyElement('access-1'));
    expect(refreshes, 1);
    expect(store.session!.refreshToken, 'refresh-1-from-refresh-0');
    expect(((outcomes.first as TokenAvailable).extra as Map)['userData'],
        {'Username': 'board-screen'});
    tokens.close();
  });

  test('a refusal signs out and a 503 keeps the session with its Retry-After',
      () async {
    final store = InMemoryCredentialStore(expired());
    final tokens = client(store);
    mode = 'unavailable';
    expect(await tokens.acquireToken(),
        const TemporarilyUnavailable(retryAfter: Duration(seconds: 7)));
    expect(store.session, isNotNull);
    await tokens.clear();
    await tokens.replaceSession(expired());
    mode = 'refused';
    expect(await tokens.acquireToken(),
        const SignInRequired(reason: 'InvalidGrant'));
    expect(store.session, isNull);
    tokens.close();
  });

  test('a server that does not answer in time is unavailable', () async {
    final tokens = client(InMemoryCredentialStore(expired()),
        timeout: const Duration(milliseconds: 300));
    mode = 'hang';
    expect(await tokens.acquireToken(),
        const TemporarilyUnavailable(retryAfter: Duration(seconds: 2)));
    tokens.close();
  });

  test('a closed port is unavailable', () async {
    final closed = await ServerSocket.bind(InternetAddress.loopbackIPv4, 0);
    final port = closed.port;
    await closed.close();
    final tokens = TokenClient(
      store: InMemoryCredentialStore(expired()),
      issuer: ShiftIdentityV2Issuer(baseUrl: 'http://127.0.0.1:$port'),
    );
    expect(await tokens.acquireToken(), isA<TemporarilyUnavailable>());
    tokens.close();
  });

  test('TokenHttpClient sends the refreshed token to the API only', () async {
    final tokens = client(InMemoryCredentialStore(expired()));
    final api = TokenHttpClient(
        tokens: tokens, apiBaseUrls: [Uri.parse('$baseUrl/api/')]);
    expect((await api.get(Uri.parse('$baseUrl/api/orders'))).body, 'ok');
    expect(apiAuthorizations, ['Bearer access-1']);
    expect((await api.get(Uri.parse('$baseUrl/other'))).statusCode, 404);
    // A request outside the API never asks for a token, even signed out.
    await tokens.clear();
    expect((await api.get(Uri.parse('$baseUrl/other'))).statusCode, 404);
    await expectLater(api.get(Uri.parse('$baseUrl/api/orders')),
        throwsA(isA<SignInRequiredException>()));
    api.close();
    tokens.close();
  });
}
