// Tests of the ShiftIdentity v2 adapter. The recorded answers are the same file the JavaScript client tests use.
// When this package sits in the ShiftIdentity repository, they and the enum tables are also checked against the C#
// source of the server, so a renamed property or a new enum member makes these tests fail.
import 'dart:convert';
import 'dart:io';

import 'package:shift_identity_token_client/shift_identity_token_client.dart';
import 'package:test/test.dart';

import 'support.dart';

final Map<String, dynamic> fixtures = (jsonDecode(
        File('test/fixtures/v2-refresh-responses.json').readAsStringSync())
    as Map<String, dynamic>)['responses'] as Map<String, dynamic>;

Map<String, dynamic> fixture(String name) =>
    fixtures[name] as Map<String, dynamic>;
Map<String, dynamic> body(String name) =>
    fixture(name)['body'] as Map<String, dynamic>;

TransportResponse response(int status, Object? body,
        [Map<String, String> headers = const {}]) =>
    TransportResponse(
        statusCode: status,
        body: body is String ? body : jsonEncode(body),
        headers: headers);

TransportResponse recorded(String name,
        [Map<String, String> headers = const {}]) =>
    response(fixture(name)['status'] as int, fixture(name)['body'], headers);

class _FakeTransport implements TokenTransport {
  _FakeTransport(this.answer, {this.onSend});
  final Object answer; // a TransportResponse or an exception to throw
  final void Function()? onSend;
  final List<TransportRequest> requests = [];

  @override
  Future<TransportResponse> send(TransportRequest request) async {
    requests.add(request);
    onSend?.call();
    final a = answer;
    if (a is TransportResponse) return a;
    throw a;
  }
}

Future<(IssuerResult, List<TransportRequest>)> refresh(
  Object answer, {
  String baseUrl = 'https://identity.example.test',
  String refreshToken = 'refresh-1',
}) async {
  final issuer = ShiftIdentityV2Issuer(baseUrl: baseUrl, now: () => t0);
  final transport = _FakeTransport(answer);
  final result = await issuer.refresh(
    SessionCredentials(
        accessToken: 'access-1',
        accessExpiresAt: t0,
        refreshToken: refreshToken),
    transport,
  );
  return (result, transport.requests);
}

Future<IssuerResult> resultOf(Object answer) async =>
    (await refresh(answer)).$1;

Matcher isSession({
  required String access,
  required String refresh,
  required Object? userData,
  DateTime? at,
}) {
  final when = at ?? t0;
  return isA<IssuerSession>().having(
    (r) => r.session.toJson(),
    'session',
    {
      'accessToken': access,
      'accessExpiresAt':
          when.add(const Duration(seconds: 900)).millisecondsSinceEpoch,
      'refreshToken': refresh,
      'refreshExpiresAt':
          when.add(const Duration(seconds: 2592000)).millisecondsSinceEpoch,
      'extra': {'userData': userData},
    },
  );
}

void main() {
  group('mapping', () {
    test('maps every recorded v2 answer', () async {
      final dto = body('session')['session'] as Map<String, dynamic>;
      final pascal =
          body('sessionPascalCase')['Session'] as Map<String, dynamic>;
      final table = <String, Matcher>{
        'session': isSession(
            access: dto['token'] as String,
            refresh: dto['refreshToken'] as String,
            userData: dto['userData']),
        'sessionPascalCase': isSession(
            access: pascal['Token'] as String,
            refresh: pascal['RefreshToken'] as String,
            userData: pascal['UserData']),
        'refusedInvalidGrantPascalCase':
            equals(const IssuerRejected(reason: 'InvalidGrant')),
        'refusedUnavailablePascalCase': equals(const IssuerUnavailable()),
        'challengePasswordChangePascalCase': equals(
            const IssuerRejected(reason: 'challenge', step: 'PasswordChange')),
        'refusedInvalidGrant':
            equals(const IssuerRejected(reason: 'InvalidGrant')),
        'refusedExpired': equals(const IssuerRejected(reason: 'Expired')),
        'refusedClientDenied':
            equals(const IssuerRejected(reason: 'ClientDenied')),
        'refusedStaleOperation':
            equals(const IssuerRejected(reason: 'StaleOperation')),
        'refusedAccountUnavailable':
            equals(const IssuerRejected(reason: 'AccountUnavailable')),
        'refusedInvalidRequest':
            equals(const IssuerRejected(reason: 'InvalidRequest')),
        'refusedInvalidGrantStringEnum':
            equals(const IssuerRejected(reason: 'InvalidGrant')),
        'refusedUnavailable': equals(const IssuerUnavailable()),
        'challengePasswordChange': equals(
            const IssuerRejected(reason: 'challenge', step: 'PasswordChange')),
        'challengeNewMfa':
            equals(const IssuerRejected(reason: 'challenge', step: 'NewMfa')),
      };
      expect(table.keys.toSet(), fixtures.keys.toSet(),
          reason: 'every recorded answer needs an expected result');
      for (final MapEntry(key: name, value: expected) in table.entries) {
        expect(await resultOf(recorded(name)), expected, reason: name);
      }
    });

    test('the enum tables map by number, by name and by camelCase name',
        () async {
      final failures = ShiftIdentityV2Issuer.failures;
      for (var index = 0; index < failures.length; index++) {
        final name = failures[index];
        final expected = ShiftIdentityV2Issuer.transientFailures.contains(name)
            ? const IssuerUnavailable()
            : IssuerRejected(reason: name);
        for (final code in [
          index,
          name,
          name[0].toLowerCase() + name.substring(1)
        ]) {
          expect(
              await resultOf(response(400,
                  {'kind': 'refused', 'code': code, 'passwordFailure': null})),
              expected,
              reason: 'code $code');
        }
      }
      final steps = ShiftIdentityV2Issuer.steps;
      for (var index = 0; index < steps.length; index++) {
        for (final step in [index, steps[index]]) {
          expect(
            await resultOf(response(200, {
              'kind': 'challenge',
              'challenge': {'step': step, 'handle': null}
            })),
            IssuerRejected(reason: 'challenge', step: steps[index]),
            reason: 'step $step',
          );
        }
      }
    });

    test(
        'network failures, timeouts and unclear answers are unavailable, never a rejection',
        () async {
      const html = '<html><body>Bad Gateway</body></html>';
      final cases = <String, (Object, IssuerResult)>{
        'network failure': (
          const TransportException('offline'),
          const IssuerUnavailable()
        ),
        'timeout': (
          const TransportException('timed out', timedOut: true),
          const IssuerUnavailable()
        ),
        'any other exception': (
          const SocketException('reset'),
          const IssuerUnavailable()
        ),
        '503 with Retry-After': (
          recorded('refusedUnavailable', {'Retry-After': '30'}),
          const IssuerUnavailable(retryAfter: Duration(seconds: 30))
        ),
        '503 with an HTTP-date Retry-After': (
          recorded('refusedUnavailable',
              {'Retry-After': 'Wed, 21 Oct 2026 07:28:00 GMT'}),
          const IssuerUnavailable()
        ),
        '500 with a refusal body': (
          response(500, body('refusedInvalidGrant')),
          const IssuerUnavailable()
        ),
        '502 from a proxy': (response(502, html), const IssuerUnavailable()),
        '504': (response(504, ''), const IssuerUnavailable()),
        '429 with Retry-After': (
          response(429, '', {'retry-after': '5'}),
          const IssuerUnavailable(retryAfter: Duration(seconds: 5))
        ),
        '408': (response(408, ''), const IssuerUnavailable()),
        'status 0': (response(0, ''), const IssuerUnavailable()),
        '404 from a wrong base URL': (
          response(404, html),
          const IssuerUnavailable()
        ),
        '401 without a v2 body': (response(401, ''), const IssuerUnavailable()),
        '400 JSON without a kind': (
          response(400, {'title': 'Bad Request'}),
          const IssuerUnavailable()
        ),
        '200 that is not JSON': (
          response(200, html),
          const IssuerUnavailable()
        ),
        '200 with a JSON array': (
          response(200, '[1,2]'),
          const IssuerUnavailable()
        ),
        '200 with another outcome kind': (
          response(200, {'kind': 'returnToLogin'}),
          const IssuerUnavailable()
        ),
        'a session outside 2xx': (
          response(400, body('session')),
          const IssuerUnavailable()
        ),
      };
      for (final MapEntry(key: name, value: (answer, expected))
          in cases.entries) {
        expect(await resultOf(answer), expected, reason: name);
      }
    });

    test('a session that is not an ordinary v2 session is unavailable',
        () async {
      final changes = <String, Map<String, Object?>>{
        'no access lifetime': {'tokenLifeTimeInSeconds': null},
        'a zero access lifetime': {'tokenLifeTimeInSeconds': 0},
        'no refresh token': {'refreshToken': ''},
        'no access token': {'token': null},
        'a restricted flow': {'flow': 2},
        'a restricted flow by name': {'flow': 'Mfa'},
      };
      for (final MapEntry(key: name, value: change) in changes.entries) {
        final dto = {
          ...body('session')['session'] as Map<String, dynamic>,
          ...change
        };
        expect(
            await resultOf(response(200, {'kind': 'session', 'session': dto})),
            const IssuerUnavailable(),
            reason: name);
      }
    });

    test('an unknown refusal code is still a rejection', () async {
      expect(await resultOf(response(400, {'kind': 'refused', 'code': 99})),
          const IssuerRejected(reason: '99'));
      expect(
          await resultOf(
              response(400, {'kind': 'refused', 'code': 'SomethingNew'})),
          const IssuerRejected(reason: 'SomethingNew'));
      expect(await resultOf(response(400, {'kind': 'refused'})),
          const IssuerRejected(reason: 'Unknown'));
      expect(await resultOf(response(200, {'kind': 'challenge'})),
          const IssuerRejected(reason: 'challenge', step: 'Unknown'));
    });
  });

  group('requests', () {
    test(
        'sends only POST /api/identity/v2/refresh with the refresh token as JSON and no Authorization header',
        () async {
      final (_, requests) =
          await refresh(recorded('session'), refreshToken: 'the-refresh-token');
      final request = requests.single;
      expect(request.method, 'POST');
      expect(request.url,
          Uri.parse('https://identity.example.test/api/identity/v2/refresh'));
      expect(request.headers,
          {'Content-Type': 'application/json', 'Accept': 'application/json'});
      expect(request.body, '{"refreshToken":"the-refresh-token"}');
      expect(request.timeout, isNull,
          reason: 'the client fills in its own timeout');

      final urls = {
        '': '/api/identity/v2/refresh',
        'https://identity.example.test/':
            'https://identity.example.test/api/identity/v2/refresh',
        'https://identity.example.test//':
            'https://identity.example.test/api/identity/v2/refresh',
        'https://example.test/identity':
            'https://example.test/identity/api/identity/v2/refresh',
      };
      for (final MapEntry(key: baseUrl, value: url) in urls.entries) {
        expect(
            (await refresh(recorded('session'), baseUrl: baseUrl))
                .$2
                .single
                .url
                .toString(),
            url,
            reason: baseUrl);
      }
    });

    test(
        'session expiry times come from the lifetimes and the clock when the answer arrives',
        () async {
      var time = t0;
      final issuer = ShiftIdentityV2Issuer(baseUrl: '', now: () => time);
      final transport = _FakeTransport(recorded('session'),
          onSend: () => time = t0.add(const Duration(seconds: 5)));
      final result = await issuer.refresh(
              const SessionCredentials(refreshToken: 'r'), transport)
          as IssuerSession;
      expect(result.session.accessExpiresAt,
          t0.add(const Duration(seconds: 5 + 900)));
      expect(result.session.refreshExpiresAt,
          t0.add(const Duration(seconds: 5 + 2592000)));
      expect(result.session.accessExpiresAt!.isUtc, isTrue);
    });
  });

  test(
      'sessionFromToken converts a TokenDTO from another v2 route into a session',
      () {
    final issuer = ShiftIdentityV2Issuer(baseUrl: '', now: () => t0);
    final dto = body('session')['session'] as Map<String, dynamic>;
    expect(issuer.sessionFromToken(dto).toJson(), {
      'accessToken': dto['token'],
      'accessExpiresAt':
          t0.add(const Duration(seconds: 900)).millisecondsSinceEpoch,
      'refreshToken': dto['refreshToken'],
      'refreshExpiresAt':
          t0.add(const Duration(seconds: 2592000)).millisecondsSinceEpoch,
      'extra': {'userData': dto['userData']},
    });
    final unknownRefresh = issuer.sessionFromToken({
      ...dto,
      'refreshTokenLifeTimeInSeconds': null,
      'flow': 'None',
      'userData': null
    });
    expect(unknownRefresh.refreshExpiresAt, isNull);
    expect(unknownRefresh.extra, {'userData': null});
    expect(issuer.sessionFromToken({...dto}..remove('flow')).accessToken,
        dto['token']);
    final pascal = body('sessionPascalCase')['Session'] as Map<String, dynamic>;
    expect(
        issuer.sessionFromToken(pascal).refreshToken, pascal['RefreshToken']);

    for (final invalid in <Map<String, Object?>>[
      {},
      {...dto, 'tokenLifeTimeInSeconds': -1},
      {...dto, 'flow': 1},
    ]) {
      expect(() => issuer.sessionFromToken(invalid), throwsArgumentError);
    }
  });

  test('the adapter declares contract version 1 and is accepted by the client',
      () {
    final issuer =
        ShiftIdentityV2Issuer(baseUrl: 'https://identity.example.test');
    expect(issuer.id, 'shift-identity-v2');
    expect(issuer.contractVersion, issuerContractVersion);
    TokenClient(store: InMemoryCredentialStore(), issuer: issuer).close();
  });

  test(
      'end to end: a stored device session, a refresh inside the skew, an outage, then a refusal',
      () async {
    final clock = Clock();
    final issuer = ShiftIdentityV2Issuer(
        baseUrl: 'https://identity.example.test/', now: clock.now);
    String? stored;
    final store = JsonCredentialStore(
      readString: () async => stored,
      writeString: (value) async => stored = value,
      deleteString: () async => stored = null,
    );
    final dto = body('session')['session'] as Map<String, dynamic>;

    // Another v2 route (for example a device sign-in) hands over a TokenDTO. The app stores it.
    await store.write(issuer.sessionFromToken(dto));

    final answers = <TransportResponse>[];
    final requests = <TransportRequest>[];
    final transport = _QueueTransport(answers, requests);
    final client = TokenClient(
        store: store, issuer: issuer, transport: transport, now: clock.now);

    final first = await client.acquireToken() as TokenAvailable;
    expect(first.token, dto['token']);
    expect(
        ((first.extra as Map)['userData'] as Map)['username'], 'board-screen');
    expect(requests, isEmpty);

    // 50 s left is inside the default skew of 60 s. A host with the framework's default JSON settings answers.
    clock.advance(const Duration(seconds: 850));
    final renewed = jsonDecode(jsonEncode(body('sessionPascalCase')))
        as Map<String, dynamic>;
    (renewed['Session'] as Map<String, dynamic>)
      ..['Token'] = 'access-3'
      ..['RefreshToken'] = 'refresh-3';
    answers.add(response(200, renewed));
    final second = await client.acquireToken() as TokenAvailable;
    expect(second.token, 'access-3');
    expect(second.expiresAt, clock.time.add(const Duration(seconds: 900)));
    expect(requests[0].url.toString(),
        'https://identity.example.test/api/identity/v2/refresh');
    expect(requests[0].timeout, const Duration(seconds: 30));
    expect((jsonDecode(requests[0].body!) as Map)['refreshToken'],
        dto['refreshToken']);
    expect((jsonDecode(stored!) as Map)['refreshToken'], 'refresh-3');

    // The access token has expired and the issuer is down. The session stays.
    clock.advance(const Duration(seconds: 900));
    answers.add(recorded('refusedUnavailablePascalCase'));
    expect(await client.acquireToken(),
        const TemporarilyUnavailable(retryAfter: Duration(seconds: 2)));
    expect(stored, isNotNull,
        reason: 'an unavailable issuer removed the session');

    // The session is no longer valid. The issuer refuses, and the session is removed.
    clock.advance(const Duration(seconds: 2));
    answers.add(recorded('refusedInvalidGrantPascalCase'));
    expect(await client.acquireToken(),
        const SignInRequired(reason: 'InvalidGrant'));
    expect(stored, isNull);
    expect(requests, hasLength(3));
  });

  group('drift against the server source', () {
    final repository = Directory('../../..').absolute;
    String? csharp(String relative) {
      final file = File('${repository.path}/$relative');
      return file.existsSync() ? file.readAsStringSync() : null;
    }

    final authOutcome =
        csharp('ShiftIdentity.Core/Authentication/AuthOutcome.cs');
    final tokenDto = csharp('ShiftIdentity.Core/DTOs/TokenDTO.cs');
    final authPurpose = csharp('ShiftIdentity.Core/Enums/AuthPurpose.cs');
    final endpoints =
        csharp('ShiftIdentity.AspNetCore/Endpoints/AdmissionEndpoints.cs');
    final jsFixtures =
        csharp('clients/javascript/test/fixtures/v2-refresh-responses.json');
    final skip = authOutcome == null ||
            tokenDto == null ||
            authPurpose == null ||
            endpoints == null
        ? 'the ShiftIdentity server source is not next to this package'
        : null;

    test(
        'the recorded answers are the same file the JavaScript client tests use',
        () {
      expect(
          jsonDecode(jsFixtures!),
          jsonDecode(File('test/fixtures/v2-refresh-responses.json')
              .readAsStringSync()));
    },
        skip: jsFixtures == null
            ? 'the JavaScript client is not next to this package'
            : null);

    test(
        'the adapter enum tables match AuthenticationFailure and AuthenticationStep',
        () {
      expect(ShiftIdentityV2Issuer.failures,
          _enumMembers(authOutcome!, 'AuthenticationFailure'));
      expect(ShiftIdentityV2Issuer.steps,
          _enumMembers(authOutcome, 'AuthenticationStep'));
      // Numbers on the wire are positions, so the enums must not set their own values.
      for (final member in [
        ...ShiftIdentityV2Issuer.failures,
        ...ShiftIdentityV2Issuer.steps
      ]) {
        expect(member, isNot(contains('=')));
      }
      // The adapter accepts flow 0 only. 0 must be AuthPurpose.None.
      expect(_enumMembers(authPurpose!, 'AuthPurpose').first,
          matches(RegExp(r'^None(\s*=\s*0)?$')));
    }, skip: skip);

    test('the wire names the adapter reads exist in the C# types', () {
      expect(
          RegExp(r'JsonPolymorphic\(TypeDiscriminatorPropertyName\s*=\s*"(\w+)"\)')
              .firstMatch(authOutcome!)!
              .group(1),
          'kind');
      final kinds = {
        for (final m
            in RegExp(r'\[JsonDerivedType\(typeof\((\w+)\),\s*"(\w+)"\)\]')
                .allMatches(authOutcome))
          m.group(1)!: m.group(2)!,
      };
      expect(kinds['SessionIssued'], 'session');
      expect(kinds['ChallengeRequired'], 'challenge');
      expect(kinds['AuthenticationRefused'], 'refused');
      expect(_recordParameters(authOutcome, 'SessionIssued'), ['Session']);
      expect(
          _recordParameters(authOutcome, 'ChallengeRequired'), ['Challenge']);
      expect(_recordParameters(authOutcome, 'AuthenticationRefused').first,
          'Code');
      expect(_recordParameters(authOutcome, 'AuthenticationChallenge').first,
          'Step');
      expect(_recordParameters(authOutcome, 'RenewSessionRequest'),
          ['RefreshToken']);
      final properties = RegExp(r'public\s+[\w<>?.]+\s+(\w+)\s*\{\s*get;')
          .allMatches(tokenDto!)
          .map((m) => m.group(1))
          .toSet();
      for (final name in [
        'Token',
        'TokenLifeTimeInSeconds',
        'RefreshToken',
        'RefreshTokenLifeTimeInSeconds',
        'Flow',
        'UserData'
      ]) {
        expect(properties, contains(name),
            reason: 'TokenDTO has no $name, which the adapter reads');
      }
    }, skip: skip);

    test('the refresh route and its status codes match the server endpoints',
        () {
      expect(endpoints!, contains(RegExp(r'MapGroup\("/api/identity/v2"\)')));
      expect(endpoints, contains(RegExp(r'group\.MapPost\("/refresh"')));
      expect(
          endpoints,
          contains(
              RegExp(r'AuthenticationFailure\.Unavailable\s*\}\s*=>\s*503')));
      expect(
          endpoints,
          contains(RegExp(
              r'AuthenticationFailure\.StaleOperation\s*\}\s*=>\s*409')));
      expect(ShiftIdentityV2Issuer.refreshPath, '/api/identity/v2/refresh');
    }, skip: skip);
  });
}

List<String> _enumMembers(String source, String name) {
  final withoutComments =
      source.replaceAll(RegExp(r'//.*$', multiLine: true), '');
  final match =
      RegExp('enum\\s+$name\\s*\\{([^}]*)\\}').firstMatch(withoutComments);
  expect(match, isNotNull, reason: 'enum $name was not found in the C# source');
  return match!
      .group(1)!
      .split(',')
      .map((m) => m.trim())
      .where((m) => m.isNotEmpty)
      .toList();
}

List<String> _recordParameters(String source, String name) {
  final match = RegExp('record\\s+$name\\s*\\(([\\s\\S]*?)\\)\\s*[:;{]')
      .firstMatch(source);
  expect(match, isNotNull,
      reason: 'record $name was not found in the C# source');
  return match!
      .group(1)!
      .replaceAll(RegExp(r'\[[^\]]*\]'), '')
      .split(',')
      .map((p) => p.replaceAll(RegExp(r'=[\s\S]*$'), '').trim())
      .where((p) => p.isNotEmpty)
      .map((p) => RegExp(r'(\w+)$').firstMatch(p)!.group(1)!)
      .toList();
}

class _QueueTransport implements TokenTransport {
  _QueueTransport(this.answers, this.requests);
  final List<TransportResponse> answers;
  final List<TransportRequest> requests;

  @override
  Future<TransportResponse> send(TransportRequest request) async {
    requests.add(request);
    return answers.removeAt(0);
  }
}
