import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shift_identity_token_client_flutter/shift_identity_token_client_flutter.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  final t0 = DateTime.utc(2026, 10, 8, 10);
  final session = SessionCredentials(
    accessToken: 'access-1',
    accessExpiresAt: t0.add(const Duration(minutes: 15)),
    refreshToken: 'refresh-1',
    refreshExpiresAt: t0.add(const Duration(days: 30)),
    extra: const {
      'userData': {'id': 'x'}
    },
  );

  setUp(() => FlutterSecureStorage.setMockInitialValues(<String, String>{}));

  test('writes, reads and clears the session under its key', () async {
    final store = SecureCredentialStore();
    expect(await store.read(), isNull);
    await store.write(session);
    expect(
      await const FlutterSecureStorage()
          .read(key: SecureCredentialStore.defaultKey),
      contains('"refreshToken":"refresh-1"'),
    );
    final back = await store.read();
    expect(back!.toJson(), session.toJson());
    await store.clear();
    expect(await store.read(), isNull);
  });

  test('keeps sessions under different keys apart', () async {
    final a = SecureCredentialStore(key: 'a');
    final b = SecureCredentialStore(key: 'b');
    await a.write(session);
    expect(await b.read(), isNull);
    expect((await a.read())!.accessToken, 'access-1');
  });

  test('reads a damaged value as no session', () async {
    FlutterSecureStorage.setMockInitialValues(
        <String, String>{SecureCredentialStore.defaultKey: '{damaged'});
    expect(await SecureCredentialStore().read(), isNull);
  });

  test('refuses an empty key', () {
    expect(() => SecureCredentialStore(key: ''), throwsArgumentError);
  });

  test('works as the store of a TokenClient', () async {
    final store = SecureCredentialStore();
    await store.write(session);
    final tokens = TokenClient(
      store: store,
      issuer: ShiftIdentityV2Issuer(baseUrl: 'https://identity.example.test'),
      now: () => t0,
    );
    final outcome = await tokens.acquireToken();
    expect(outcome, isA<TokenAvailable>());
    expect((outcome as TokenAvailable).token, 'access-1');
    await tokens.clear();
    expect(await store.read(), isNull);
    tokens.close();
  });
}
