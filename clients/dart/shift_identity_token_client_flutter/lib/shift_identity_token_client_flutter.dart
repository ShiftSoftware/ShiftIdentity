/// ShiftIdentity.TokenClient for Flutter.
///
/// Everything from `shift_identity_token_client` (re-exported), plus
/// [SecureCredentialStore], which keeps the session in the platform's secure
/// storage.
library;

export 'package:shift_identity_token_client/shift_identity_token_client.dart';

export 'src/secure_credential_store.dart';
