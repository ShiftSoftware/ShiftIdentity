import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:shift_identity_token_client/shift_identity_token_client.dart';

/// A [CredentialStore] on `flutter_secure_storage`: the Keychain on iOS and
/// macOS, encrypted storage on Android, libsecret on Linux, DPAPI-protected
/// storage on Windows, and (with that package's limits) the browser on the web.
///
/// It keeps the session as JSON under [key], reads a damaged value as no
/// session, and runs operations in the order they are called.
///
/// ```dart
/// final tokens = TokenClient(
///   store: SecureCredentialStore(),
///   issuer: ShiftIdentityV2Issuer(baseUrl: 'https://identity.example.com'),
/// );
/// ```
///
/// Pass your own [FlutterSecureStorage] to choose platform options, for
/// example the iOS keychain accessibility:
///
/// ```dart
/// SecureCredentialStore(
///   storage: const FlutterSecureStorage(
///     iOptions: IOSOptions(accessibility: KeychainAccessibility.first_unlock),
///   ),
/// )
/// ```
final class SecureCredentialStore implements CredentialStore {
  /// Creates the store. Use a different [key] for each session the app keeps
  /// at the same time (for example per issuer or per account).
  SecureCredentialStore({
    this.key = defaultKey,
    FlutterSecureStorage? storage,
  }) : storage = storage ?? const FlutterSecureStorage() {
    if (key.isEmpty) throw ArgumentError.value(key, 'key', 'must not be empty');
    _json = JsonCredentialStore(
      readString: () => this.storage.read(key: key),
      writeString: (value) => this.storage.write(key: key, value: value),
      deleteString: () => this.storage.delete(key: key),
    );
  }

  /// The key used when none is given.
  static const String defaultKey = 'shift_identity.session';

  /// The secure storage key that holds the session.
  final String key;

  /// The underlying secure storage.
  final FlutterSecureStorage storage;

  late final JsonCredentialStore _json;

  @override
  Future<SessionCredentials?> read() => _json.read();

  @override
  Future<void> write(SessionCredentials session) => _json.write(session);

  @override
  Future<void> clear() => _json.clear();
}
