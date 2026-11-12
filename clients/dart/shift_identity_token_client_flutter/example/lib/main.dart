// A small Flutter app that keeps a ShiftIdentity session alive and calls an API with it.
//
// Set identityBaseUrl and apiUrl below, then run it (`flutter create .` once in this folder adds the platform
// folders). Sign-in itself is app-specific: this example accepts the JSON of a v2 TokenDTO, for example the
// "Session" of a v2/login answer, and hands it to the client the same way a real sign-in page would.
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:shift_identity_token_client_flutter/shift_identity_token_client_flutter.dart';

const identityBaseUrl = 'https://identity.example.com';
const apiUrl = 'https://api.example.com/api/me';

// One client for the whole app.
final issuer = ShiftIdentityV2Issuer(baseUrl: identityBaseUrl);
final tokens = TokenClient(store: SecureCredentialStore(), issuer: issuer);
final api = TokenHttpClient(
  tokens: tokens,
  apiBaseUrls: [Uri.parse(apiUrl).replace(path: '/')],
);

void main() => runApp(const ExampleApp());

/// The app: a sign-in page and a home page.
class ExampleApp extends StatelessWidget {
  /// Creates the app.
  const ExampleApp({super.key});

  @override
  Widget build(BuildContext context) => MaterialApp(
        title: 'TokenClient example',
        theme: ThemeData(colorSchemeSeed: Colors.indigo, useMaterial3: true),
        home: const StartPage(),
      );
}

/// Decides where to start: home when a usable session is stored, sign-in otherwise.
class StartPage extends StatelessWidget {
  /// Creates the page.
  const StartPage({super.key});

  @override
  Widget build(BuildContext context) => FutureBuilder<TokenOutcome>(
        future: tokens.acquireToken(),
        builder: (context, snapshot) => switch (snapshot.data) {
          null =>
            const Scaffold(body: Center(child: CircularProgressIndicator())),
          SignInRequired() => const SignInPage(),
          // A temporarily unavailable issuer is not a sign-out: keep the user in the app.
          TokenAvailable() || TemporarilyUnavailable() => const HomePage(),
        },
      );
}

/// Stands in for the app's real sign-in page.
class SignInPage extends StatefulWidget {
  /// Creates the page.
  const SignInPage({super.key});

  @override
  State<SignInPage> createState() => _SignInPageState();
}

class _SignInPageState extends State<SignInPage> {
  final _tokenDto = TextEditingController();
  String? _error;

  Future<void> _signIn() async {
    try {
      final dto = jsonDecode(_tokenDto.text) as Map<String, Object?>;
      // This is the one line a real sign-in page needs after it gets a TokenDTO.
      await tokens.replaceSession(issuer.sessionFromToken(dto));
      if (!mounted) return;
      await Navigator.of(context).pushReplacement(
          MaterialPageRoute<void>(builder: (_) => const HomePage()));
    } on FormatException catch (e) {
      setState(() => _error = 'Not JSON: ${e.message}');
    } on ArgumentError catch (e) {
      setState(() => _error = '${e.message}');
    }
  }

  @override
  void dispose() {
    _tokenDto.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: const Text('Sign in')),
        body: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            const Text('Paste the JSON of a ShiftIdentity v2 TokenDTO.'),
            const SizedBox(height: 12),
            TextField(
              controller: _tokenDto,
              maxLines: 8,
              decoration: InputDecoration(
                  border: const OutlineInputBorder(), errorText: _error),
            ),
            const SizedBox(height: 12),
            FilledButton(
                onPressed: _signIn, child: const Text('Use this session')),
          ],
        ),
      );
}

/// Calls the API through [TokenHttpClient] and reacts to the outcome.
class HomePage extends StatefulWidget {
  /// Creates the page.
  const HomePage({super.key});

  @override
  State<HomePage> createState() => _HomePageState();
}

class _HomePageState extends State<HomePage> {
  String _status = 'Not called yet.';

  Future<void> _callApi() async {
    setState(() => _status = 'Calling…');
    try {
      final response = await api.get(Uri.parse(apiUrl));
      setState(() => _status = 'HTTP ${response.statusCode}\n${response.body}');
    } on SignInRequiredException catch (e) {
      // The session ended (refused, expired or signed out elsewhere). The app decides; here, back to sign-in.
      _toSignIn('Please sign in again (${e.outcome.reason}).');
    } on TokenTemporarilyUnavailableException catch (e) {
      setState(() => _status =
          'Identity is unreachable. Retry in ${e.outcome.retryAfter.inSeconds} s.');
    } on Exception catch (e) {
      setState(() => _status = 'The API call failed: $e');
    }
  }

  Future<void> _signOut() async {
    await tokens.clear();
    _toSignIn('Signed out.');
  }

  void _toSignIn(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context)
        .showSnackBar(SnackBar(content: Text(message)));
    Navigator.of(context).pushReplacement(
        MaterialPageRoute<void>(builder: (_) => const SignInPage()));
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(
          title: const Text('Home'),
          actions: [
            IconButton(
                onPressed: _signOut,
                icon: const Icon(Icons.logout),
                tooltip: 'Sign out')
          ],
        ),
        body: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            FilledButton(
                onPressed: _callApi, child: const Text('Call the API')),
            const SizedBox(height: 16),
            SelectableText(_status),
          ],
        ),
      );
}
