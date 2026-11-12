import 'package:http/http.dart' as http;

import 'token_client.dart';
import 'token_outcome.dart';

/// An `http.Client` that adds `Authorization: Bearer <access token>` to the
/// requests for your API, and leaves every other request untouched.
///
/// Before each request to a managed destination it calls
/// [TokenClient.acquireToken]:
/// * [TokenAvailable]: the token is attached and the request is sent.
/// * [SignInRequired]: the request is not sent; [SignInRequiredException] is
///   thrown.
/// * [TemporarilyUnavailable]: the request is not sent;
///   [TokenTemporarilyUnavailableException] is thrown.
///
/// Catch [TokenAcquisitionException] in one place (a repository base class,
/// an error boundary) to send the user to sign-in, instead of checking at
/// every call.
///
/// ```dart
/// final api = TokenHttpClient(
///   tokens: tokenClient,
///   apiBaseUrls: [Uri.parse('https://api.example.com/')],
/// );
/// final response = await api.get(Uri.parse('https://api.example.com/orders'));
/// ```
///
/// Which requests are managed is decided by their URL only, never by an
/// existing `Authorization` header: a request that already has one is sent as
/// it is. A `401` from your API is returned as a normal response; it is not
/// treated as a revoked session, because the next refresh tells that apart.
class TokenHttpClient extends http.BaseClient {
  /// Creates the client. Give exactly one of:
  /// * [apiBaseUrls]: a request is managed when its scheme, host and port
  ///   match one of them and its path starts with that base path (on a
  ///   segment boundary);
  /// * [isManaged]: your own test of the request URL.
  ///
  /// Requests are sent through [inner] (default: a new `http.Client`, closed
  /// by [close]; a given client is left open).
  TokenHttpClient({
    required TokenClient tokens,
    Iterable<Uri>? apiBaseUrls,
    bool Function(Uri url)? isManaged,
    http.Client? inner,
  })  : _tokens = tokens,
        _isManaged = _matcher(apiBaseUrls, isManaged),
        _inner = inner ?? http.Client(),
        _ownsInner = inner == null;

  final TokenClient _tokens;
  final bool Function(Uri url) _isManaged;
  final http.Client _inner;
  final bool _ownsInner;

  /// Whether a request to [url] gets the access token.
  bool manages(Uri url) => _isManaged(url);

  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) async {
    if (!_isManaged(request.url) ||
        request.headers.containsKey('authorization')) {
      return _inner.send(request);
    }
    switch (await _tokens.acquireToken()) {
      case TokenAvailable(:final token):
        request.headers['Authorization'] = 'Bearer $token';
        return _inner.send(request);
      case final SignInRequired outcome:
        throw SignInRequiredException(outcome, request.url);
      case final TemporarilyUnavailable outcome:
        throw TokenTemporarilyUnavailableException(outcome, request.url);
    }
  }

  @override
  void close() {
    if (_ownsInner) _inner.close();
  }
}

/// No access token could be attached, so the request was not sent.
sealed class TokenAcquisitionException implements Exception {
  const TokenAcquisitionException(this.url);

  /// The URL of the request that was not sent.
  final Uri url;
}

/// The user must sign in again. The request was not sent.
final class SignInRequiredException extends TokenAcquisitionException {
  /// Creates the exception.
  const SignInRequiredException(this.outcome, Uri url) : super(url);

  /// The outcome, with the reason and any outstanding step.
  final SignInRequired outcome;

  @override
  String toString() => 'SignInRequiredException: ${outcome.reason}'
      '${outcome.step == null ? '' : ' (step ${outcome.step})'}; $url was not requested.';
}

/// The issuer could not be reached and the access token has expired. The
/// session is kept; try again after [TemporarilyUnavailable.retryAfter]. The
/// request was not sent.
final class TokenTemporarilyUnavailableException
    extends TokenAcquisitionException {
  /// Creates the exception.
  const TokenTemporarilyUnavailableException(this.outcome, Uri url)
      : super(url);

  /// The outcome, with the retry delay.
  final TemporarilyUnavailable outcome;

  @override
  String toString() => 'TokenTemporarilyUnavailableException: retry after '
      '${outcome.retryAfter.inSeconds} s; $url was not requested.';
}

bool Function(Uri url) _matcher(
    Iterable<Uri>? apiBaseUrls, bool Function(Uri url)? isManaged) {
  if ((apiBaseUrls == null) == (isManaged == null)) {
    throw ArgumentError('Give exactly one of apiBaseUrls and isManaged.');
  }
  if (isManaged != null) return isManaged;
  final bases = apiBaseUrls!.toList(growable: false);
  if (bases.isEmpty) {
    throw ArgumentError.value(bases, 'apiBaseUrls', 'must not be empty');
  }
  for (final base in bases) {
    if (!base.hasScheme || base.host.isEmpty) {
      throw ArgumentError.value(base, 'apiBaseUrls',
          'must be absolute URLs, such as https://api.example.com/');
    }
  }
  return (url) => bases.any((base) => _under(url, base));
}

bool _under(Uri url, Uri base) {
  if (url.scheme.toLowerCase() != base.scheme.toLowerCase()) return false;
  if (url.host.toLowerCase() != base.host.toLowerCase()) return false;
  if (url.port != base.port) return false;
  var prefix = base.path;
  while (prefix.endsWith('/')) {
    prefix = prefix.substring(0, prefix.length - 1);
  }
  if (prefix.isEmpty) return true;
  final path = url.path;
  return path == prefix || path.startsWith('$prefix/');
}
