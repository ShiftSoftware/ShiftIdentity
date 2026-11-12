import 'dart:async';
import 'dart:convert';

import 'package:shift_identity_token_client/shift_identity_token_client.dart';
import 'package:test/test.dart';

import 'support.dart';

void main() {
  group('SessionCredentials', () {
    test('round-trips through the JSON shape the JavaScript client stores', () {
      final original = session(extra: {
        'userData': {'id': 'x'}
      });
      final json = jsonDecode(jsonEncode(original.toJson()));
      expect(json, {
        'accessToken': 'access-1',
        'accessExpiresAt':
            t0.add(const Duration(minutes: 15)).millisecondsSinceEpoch,
        'refreshToken': 'refresh-1',
        'refreshExpiresAt':
            t0.add(const Duration(days: 30)).millisecondsSinceEpoch,
        'extra': {
          'userData': {'id': 'x'}
        },
      });
      final back = SessionCredentials.tryFromJson(json)!;
      expect(back.toJson(), original.toJson());
      expect(back.accessExpiresAt!.isUtc, isTrue);
    });

    test(
        'reads a session written by the JavaScript client, where times may be doubles',
        () {
      final back = SessionCredentials.tryFromJson(jsonDecode(
          '{"accessToken":"a","accessExpiresAt":1791453600000.0,"refreshToken":"r","refreshExpiresAt":null,"extra":null}'))!;
      expect(back.accessExpiresAt,
          DateTime.fromMillisecondsSinceEpoch(1791453600000, isUtc: true));
      expect(back.refreshExpiresAt, isNull);
    });

    test(
        'anything without a token is not a session, and wrong field types read as missing',
        () {
      for (final value in [
        null,
        'text',
        5,
        <Object?>[],
        <String, Object?>{},
        {'accessToken': ''},
        {'accessToken': 7}
      ]) {
        expect(SessionCredentials.tryFromJson(value), isNull, reason: '$value');
      }
      final partial = SessionCredentials.tryFromJson(
          {'refreshToken': 'r', 'accessExpiresAt': 'soon'})!;
      expect(partial.accessExpiresAt, isNull);
      expect(partial.isComplete, isFalse);
      expect(partial.isReadable, isTrue);
    });

    test('toString never prints a token', () {
      expect('${session()}', isNot(contains('access-1')));
      expect('${session()}', isNot(contains('refresh-1')));
      expect('${TokenAvailable(token: 'secret', expiresAt: t0)}',
          isNot(contains('secret')));
    });
  });

  group('InMemoryCredentialStore', () {
    test('writes, reads and clears', () async {
      final store = InMemoryCredentialStore();
      expect(await store.read(), isNull);
      await store.write(session());
      expect((await store.read())!.accessToken, 'access-1');
      await store.clear();
      expect(store.session, isNull);
    });
  });

  group('JsonCredentialStore', () {
    late String? stored;
    late JsonCredentialStore store;
    setUp(() {
      stored = null;
      store = JsonCredentialStore(
        readString: () async => stored,
        writeString: (value) async => stored = value,
        deleteString: () async => stored = null,
      );
    });

    test('writes, reads and clears the session as JSON', () async {
      await store.write(session());
      expect(jsonDecode(stored!), session().toJson());
      expect((await store.read())!.refreshToken, 'refresh-1');
      await store.clear();
      expect(stored, isNull);
      expect(await store.read(), isNull);
    });

    test('a damaged or non-object value reads as no session', () async {
      for (final value in ['{not json', '[1]', '"text"', '', 'null', '{}']) {
        stored = value;
        expect(await store.read(), isNull, reason: value);
      }
    });

    test('a storage failure is passed on, and later operations still run',
        () async {
      var fail = true;
      final failing = JsonCredentialStore(
        readString: () async => fail ? throw StateError('locked') : null,
        writeString: (value) async {},
        deleteString: () async {},
      );
      await expectLater(failing.read(), throwsStateError);
      fail = false;
      expect(await failing.read(), isNull);
    });

    test(
        'operations take effect in the order they are called, even over slow storage',
        () async {
      final log = <String>[];
      final gate = Completer<void>();
      String? value;
      final slow = JsonCredentialStore(
        readString: () async {
          log.add('read');
          return value;
        },
        writeString: (v) async {
          log.add('write start');
          await gate.future;
          value = v;
          log.add('write end');
        },
        deleteString: () async {
          log.add('delete');
          value = null;
        },
      );
      final write = slow.write(session());
      final read = slow.read();
      final clear = slow.clear();
      await settle();
      expect(log, ['write start']);
      gate.complete();
      await Future.wait([write, read, clear]);
      expect(log, ['write start', 'write end', 'read', 'delete']);
      expect((await read)!.accessToken, 'access-1');
      expect(value, isNull);
    });

    test('a failing write makes acquireToken throw after a refresh', () async {
      final failing = JsonCredentialStore(
        readString: () async =>
            jsonEncode(session(accessLeft: Duration.zero).toJson()),
        writeString: (value) async => throw StateError('quota'),
        deleteString: () async {},
      );
      final issuer = FakeIssuer()
        ..queued.add(IssuerSession(session(access: 'access-2')));
      final client = TokenClient(store: failing, issuer: issuer, now: () => t0);
      await expectLater(
          client.acquireToken(), throwsA(isA<CredentialStoreException>()));
      client.close();
    });
  });
}
