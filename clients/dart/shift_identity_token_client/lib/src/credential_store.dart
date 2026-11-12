import 'dart:async';
import 'dart:convert';

import 'session_credentials.dart';

/// Where the app keeps the session. The app owns how and where it persists;
/// the client decides when to read, write and clear.
///
/// Contract:
/// * Operations take effect in the order they are called.
/// * `write` completes only once the session is stored. The client reports a
///   new token only after `write` completes.
/// * A failure is reported by throwing (or completing with an error). The
///   client passes it on as a [CredentialStoreException].
/// * `read` returns `null` when nothing (or nothing readable) is stored.
///
/// Ready-made: [InMemoryCredentialStore], [JsonCredentialStore], and
/// `SecureCredentialStore` in `shift_identity_token_client_flutter`.
abstract interface class CredentialStore {
  /// Reads the stored session, or `null`.
  Future<SessionCredentials?> read();

  /// Stores [session], replacing any stored session.
  Future<void> write(SessionCredentials session);

  /// Removes the stored session.
  Future<void> clear();
}

/// Keeps the session in memory only. For tests, and for apps that must not
/// keep a session across restarts.
final class InMemoryCredentialStore implements CredentialStore {
  /// Creates the store, optionally holding [session] already.
  InMemoryCredentialStore([SessionCredentials? session]) : _session = session;

  SessionCredentials? _session;

  /// The stored session, for inspection.
  SessionCredentials? get session => _session;

  @override
  Future<SessionCredentials?> read() async => _session;

  @override
  Future<void> write(SessionCredentials session) async => _session = session;

  @override
  Future<void> clear() async => _session = null;
}

/// A store over any key-value storage that holds one string: shared
/// preferences, a file, a database row, a keychain item.
///
/// It keeps the session as the JSON of [SessionCredentials.toJson], reads a
/// damaged value as no session, and runs operations one after another in the
/// order they were called, even when the underlying storage does not.
///
/// ```dart
/// final prefs = await SharedPreferences.getInstance();
/// final store = JsonCredentialStore(
///   readString: () async => prefs.getString('identity.session'),
///   writeString: (value) => prefs.setString('identity.session', value),
///   deleteString: () => prefs.remove('identity.session'),
/// );
/// ```
///
/// A refresh token is a long-lived credential. On a phone, prefer
/// `SecureCredentialStore` from `shift_identity_token_client_flutter`.
final class JsonCredentialStore implements CredentialStore {
  /// Creates the store from three functions over one stored string.
  JsonCredentialStore({
    required Future<String?> Function() readString,
    required Future<void> Function(String value) writeString,
    required Future<void> Function() deleteString,
  })  : _readString = readString,
        _writeString = writeString,
        _deleteString = deleteString;

  final Future<String?> Function() _readString;
  final Future<void> Function(String value) _writeString;
  final Future<void> Function() _deleteString;

  // The last operation. Each new operation starts after it ends.
  Future<void> _tail = Future<void>.value();

  Future<T> _serial<T>(Future<T> Function() action) {
    final result = _tail.then((_) => action());
    _tail = result.then<void>((_) {}, onError: (Object _) {});
    return result;
  }

  @override
  Future<SessionCredentials?> read() => _serial(() async {
        final raw = await _readString();
        if (raw == null || raw.isEmpty) return null;
        try {
          return SessionCredentials.tryFromJson(jsonDecode(raw));
        } on FormatException {
          // A damaged value is the same as no session.
          return null;
        }
      });

  @override
  Future<void> write(SessionCredentials session) =>
      _serial(() => _writeString(jsonEncode(session.toJson())));

  @override
  Future<void> clear() => _serial(_deleteString);
}

/// The credential store failed. `TokenClient.acquireToken` and
/// `TokenClient.clear` throw it; it is a programmer or platform error, never
/// an authentication condition.
final class CredentialStoreException implements Exception {
  /// Creates the exception.
  const CredentialStoreException(this.operation, this.cause,
      [this.causeStackTrace]);

  /// `read`, `write` or `clear`.
  final String operation;

  /// What the store threw.
  final Object cause;

  /// Where the store threw it.
  final StackTrace? causeStackTrace;

  @override
  String toString() =>
      'CredentialStoreException: the credential store failed to $operation the session: $cause';
}
