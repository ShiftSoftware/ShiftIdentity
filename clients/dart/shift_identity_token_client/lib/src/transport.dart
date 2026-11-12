import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

/// One HTTP request that an issuer adapter asks the client to send.
final class TransportRequest {
  /// Creates the request.
  const TransportRequest({
    required this.method,
    required this.url,
    this.headers = const <String, String>{},
    this.body,
    this.timeout,
  });

  /// The HTTP method, for example `POST`.
  final String method;

  /// The absolute (or, on the web, page-relative) URL.
  final Uri url;

  /// The request headers.
  final Map<String, String> headers;

  /// The request body, or `null` for none.
  final String? body;

  /// How long to wait for the whole response. The client fills it in with its
  /// own timeout when the adapter leaves it `null`.
  final Duration? timeout;

  /// Returns a copy with [timeout] replaced.
  TransportRequest withTimeout(Duration timeout) => TransportRequest(
        method: method,
        url: url,
        headers: headers,
        body: body,
        timeout: timeout,
      );

  @override
  String toString() => 'TransportRequest($method $url)';
}

/// The answer to a [TransportRequest]. Any status code is an answer; only a
/// failure to get one is a [TransportException].
final class TransportResponse {
  /// Creates the response. Header names are matched without regard to case.
  TransportResponse({
    required this.statusCode,
    this.body = '',
    Map<String, String> headers = const <String, String>{},
  }) : headers = <String, String>{
          for (final entry in headers.entries)
            entry.key.toLowerCase(): entry.value,
        };

  /// The HTTP status code.
  final int statusCode;

  /// The body, decoded as UTF-8.
  final String body;

  /// The response headers, with lower-case names.
  final Map<String, String> headers;

  /// The value of the header [name] (any case), or `null`.
  String? header(String name) => headers[name.toLowerCase()];

  @override
  String toString() => 'TransportResponse($statusCode)';
}

/// No response arrived: offline, DNS, TLS, a refused connection, or a timeout.
final class TransportException implements Exception {
  /// Creates the exception.
  const TransportException(this.message, {this.timedOut = false, this.cause});

  /// What happened.
  final String message;

  /// Whether the request timed out.
  final bool timedOut;

  /// The underlying error, if any.
  final Object? cause;

  @override
  String toString() =>
      'TransportException: $message${cause == null ? '' : ' ($cause)'}';
}

/// Sends the requests of an issuer adapter. Replace it to route refreshes
/// through your own HTTP stack (for example Dio, or a client with certificate
/// pinning) or to fake the issuer in tests.
abstract interface class TokenTransport {
  /// Sends [request]. Returns any response, whatever its status code, and
  /// throws a [TransportException] when no response arrives in time.
  Future<TransportResponse> send(TransportRequest request);
}

/// The default [TokenTransport], on `package:http`. It works on every
/// platform, including the web.
final class HttpTokenTransport implements TokenTransport {
  /// Creates the transport. Without [client] it creates one and closes it in
  /// [close]; a given client is left open.
  HttpTokenTransport([http.Client? client])
      : _client = client ?? http.Client(),
        _ownsClient = client == null;

  final http.Client _client;
  final bool _ownsClient;

  @override
  Future<TransportResponse> send(TransportRequest request) async {
    final timeout = request.timeout;
    Future<TransportResponse> exchange() async {
      final message = http.Request(request.method, request.url)
        ..headers.addAll(request.headers);
      final body = request.body;
      if (body != null) message.bodyBytes = utf8.encode(body);
      final response =
          await http.Response.fromStream(await _client.send(message));
      return TransportResponse(
        statusCode: response.statusCode,
        // JSON is UTF-8. package:http would fall back to Latin-1 when the
        // content type names no charset.
        body: utf8.decode(response.bodyBytes, allowMalformed: true),
        headers: response.headers,
      );
    }

    try {
      final pending = exchange();
      if (timeout == null || timeout <= Duration.zero) return await pending;
      return await pending.timeout(timeout);
    } on TimeoutException catch (error) {
      throw TransportException('the request timed out.',
          timedOut: true, cause: error);
    } on TransportException {
      rethrow;
    } on Exception catch (error) {
      // http.ClientException, SocketException, HandshakeException and the like.
      throw TransportException('the request failed before a response arrived.',
          cause: error);
    }
  }

  /// Closes the HTTP client if this transport created it.
  void close() {
    if (_ownsClient) _client.close();
  }
}
