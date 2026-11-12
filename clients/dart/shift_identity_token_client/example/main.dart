// A command-line walk through ShiftIdentity.TokenClient.
//
//   dart run example/main.dart <identity base URL> <refresh token>
//
// It stores a session that has only a refresh token, so the first
// acquireToken refreshes it with ShiftIdentity v2, then calls an API through
// TokenHttpClient. In a Flutter app, use SecureCredentialStore from
// shift_identity_token_client_flutter instead of the in-memory store.
import 'dart:io';

import 'package:shift_identity_token_client/shift_identity_token_client.dart';

Future<void> main(List<String> arguments) async {
  if (arguments.length < 2) {
    stderr.writeln(
        'Usage: dart run example/main.dart <identity base URL> <refresh token> [API URL]');
    exitCode = 64;
    return;
  }
  final baseUrl = arguments[0];
  final refreshToken = arguments[1];

  final issuer = ShiftIdentityV2Issuer(baseUrl: baseUrl);
  final store = InMemoryCredentialStore();
  final tokens = TokenClient(store: store, issuer: issuer);

  // After a sign-in, store the session. With a TokenDTO from v2/login, use
  // issuer.sessionFromToken(tokenDto) instead.
  await tokens.replaceSession(SessionCredentials(refreshToken: refreshToken));

  // 1. Direct access: ask for a token and react to the outcome.
  switch (await tokens.acquireToken()) {
    case TokenAvailable(:final expiresAt):
      stdout.writeln('Token available until $expiresAt.');
    case SignInRequired(:final reason, :final step):
      stdout.writeln(
          'Sign in again: $reason${step == null ? '' : ' (step $step)'}.');
      return;
    case TemporarilyUnavailable(:final retryAfter):
      stdout.writeln(
          'The issuer is unavailable. Try again in ${retryAfter.inSeconds} s.');
      return;
  }

  // 2. Automatic HTTP: requests to the API get the token attached.
  if (arguments.length > 2) {
    final apiUrl = Uri.parse(arguments[2]);
    final api = TokenHttpClient(
      tokens: tokens,
      apiBaseUrls: [apiUrl.replace(path: '/')],
    );
    try {
      final response = await api.get(apiUrl);
      stdout.writeln('GET $apiUrl -> ${response.statusCode}');
    } on SignInRequiredException catch (error) {
      stdout.writeln('Sign in again: ${error.outcome.reason}.');
    } on TokenTemporarilyUnavailableException catch (error) {
      stdout.writeln('Try again in ${error.outcome.retryAfter.inSeconds} s.');
    } finally {
      api.close();
    }
  }

  // Sign out.
  await tokens.clear();
  tokens.close();
}
