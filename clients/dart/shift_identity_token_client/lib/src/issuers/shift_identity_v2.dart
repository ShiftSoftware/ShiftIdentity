import 'dart:convert';

import '../session_credentials.dart';
import '../token_issuer.dart';
import '../transport.dart';

/// The ShiftIdentity v2 issuer adapter.
///
/// It calls only `POST {baseUrl}/api/identity/v2/refresh` with
/// `{"refreshToken": "..."}` and no `Authorization` header, and maps the
/// answer:
///
/// | v2 answer | Result |
/// |---|---|
/// | 2xx `kind: session` with an ordinary session (`flow` None, positive `tokenLifeTimeInSeconds`) | [IssuerSession], with `extra: {'userData': ...}` |
/// | `kind: refused` with any code except `Unavailable` and `AttemptsExhausted` | [IssuerRejected], `reason` = the code name |
/// | `kind: challenge` (an outstanding step) | [IssuerRejected], `reason: 'challenge'`, `step` = the step name |
/// | Network failure, timeout, 5xx, 408, 429, `refused` with `Unavailable` or `AttemptsExhausted`, or any other answer | [IssuerUnavailable] (with `Retry-After` in seconds) |
///
/// Codes and steps are read as numbers (the server default) or as names, and
/// property names in any letter case: a host with the Shift framework's
/// default JSON settings writes PascalCase, a plain ASP.NET Core host writes
/// camelCase.
final class ShiftIdentityV2Issuer implements TokenIssuer {
  /// Creates the adapter. [baseUrl] is the ShiftIdentity API origin, for
  /// example `https://identity.example.com` (a path prefix such as
  /// `https://example.com/identity` works too). On the web, `''` means the
  /// page's own origin. [now] is for tests.
  ShiftIdentityV2Issuer({required String baseUrl, DateTime Function()? now})
      : refreshUrl = Uri.parse(_trimTrailingSlashes(baseUrl) + refreshPath),
        _now = now ?? DateTime.now;

  /// The adapter id.
  static const String issuerId = 'shift-identity-v2';

  /// The refresh route, relative to the base URL.
  static const String refreshPath = '/api/identity/v2/refresh';

  /// `AuthenticationFailure` in ShiftIdentity.Core, in wire order. The server
  /// writes enums as numbers (positions) unless the host adds a string enum
  /// converter. A test compares this list with the C# source.
  static const List<String> failures = <String>[
    'InvalidRequest', 'InvalidProof', 'InvalidGrant', 'StaleOperation',
    'Expired', 'AttemptsExhausted', //
    'AccountUnavailable', 'ClientDenied', 'Unavailable', 'InvalidNewPassword',
    'DuplicateIdentifier', //
    'ReauthenticationRequired', 'ProviderAccountNotFound',
    'ProviderEmailUnverified', //
    'AuthorizationPending', 'SlowDown', 'AccessDenied', 'ExpiredToken',
    'DeviceSignInNotAllowed', //
    'RequiredPasswordChangeNotAllowed', 'AccessNotCovered',
    'PasswordSignInDisabled', //
    'ProviderNotAllowedForAccount',
  ];

  /// `AuthenticationStep` in ShiftIdentity.Core, in wire order.
  static const List<String> steps = <String>[
    'ExistingMfa', 'PasswordChange', 'MfaRecovery', 'NewMfa',
    'EmailVerification', 'Password', //
    'ProviderConfirmation',
  ];

  /// Refusals that mean "try again later". Every other refusal means the
  /// session cannot be renewed.
  static const Set<String> transientFailures = <String>{
    'Unavailable',
    'AttemptsExhausted'
  };

  /// The full URL the adapter posts to.
  final Uri refreshUrl;

  final DateTime Function() _now;

  @override
  String get id => issuerId;

  @override
  int get contractVersion => 1;

  @override
  Future<IssuerResult> refresh(
      SessionCredentials session, TokenTransport transport) async {
    final TransportResponse response;
    try {
      response = await transport.send(TransportRequest(
        method: 'POST',
        url: refreshUrl,
        headers: const <String, String>{
          'Content-Type': 'application/json',
          'Accept': 'application/json'
        },
        body:
            jsonEncode(<String, Object?>{'refreshToken': session.refreshToken}),
      ));
    } on Exception {
      // Offline, timeout, TLS: the issuer gave no answer.
      return const IssuerUnavailable();
    }
    return interpret(response, _now());
  }

  /// Converts a `TokenDTO` from another v2 route (for example `v2/login`, or a
  /// device sign-in) into a session for the store, with expiry times from the
  /// lifetimes and this device's clock.
  ///
  /// Throws an [ArgumentError] when [token] is not an ordinary v2 session: it
  /// needs `token`, `refreshToken`, a positive `tokenLifeTimeInSeconds`, and
  /// `flow` None.
  SessionCredentials sessionFromToken(Map<String, Object?> token) {
    final session = _sessionFromTokenDto(token, _now());
    if (session == null) {
      throw ArgumentError.value(
        '<TokenDTO>',
        'token',
        'is not an ordinary v2 session. It needs token, refreshToken, a positive '
            'tokenLifeTimeInSeconds, and flow None',
      );
    }
    return session;
  }

  /// Maps one HTTP answer of `v2/refresh`, received at [receivedAt], to an
  /// adapter result. Only a definite answer from the issuer (a refusal or an
  /// outstanding step) means "sign in again". Anything unclear is unavailable.
  static IssuerResult interpret(
      TransportResponse response, DateTime receivedAt) {
    final status = response.statusCode;
    if (status == 0 || status >= 500 || status == 408 || status == 429) {
      return _unavailable(response);
    }

    final outcome = _parseJsonObject(response.body);
    final kind = _field(outcome, 'kind');

    if (kind == 'session') {
      final session = status >= 200 && status < 300
          ? _sessionFromTokenDto(_field(outcome, 'session'), receivedAt)
          : null;
      return session == null ? _unavailable(response) : IssuerSession(session);
    }
    if (kind == 'refused') {
      final code = _enumName(failures, _field(outcome, 'code'));
      return transientFailures.contains(code)
          ? _unavailable(response)
          : IssuerRejected(reason: code);
    }
    if (kind == 'challenge') {
      return IssuerRejected(
          reason: 'challenge',
          step: _enumName(steps, _field(_field(outcome, 'challenge'), 'step')));
    }
    return _unavailable(response);
  }

  @override
  String toString() => 'ShiftIdentityV2Issuer($refreshUrl)';
}

String _trimTrailingSlashes(String url) {
  var end = url.length;
  while (end > 0 && url.codeUnitAt(end - 1) == 0x2F) {
    end--;
  }
  return url.substring(0, end);
}

// Reads a property by name in any letter case. A host with the framework's
// default JSON settings (ShiftEntity's JsonNamingPolicy is null) writes
// PascalCase names such as Session, Code and Token. A host with the ASP.NET
// Core defaults writes camelCase. The "kind" discriminator is the same in both.
Object? _field(Object? value, String name) {
  if (value is! Map) return null;
  if (value.containsKey(name)) return value[name];
  final lower = name.toLowerCase();
  for (final entry in value.entries) {
    final key = entry.key;
    if (key is String && key.toLowerCase() == lower) return entry.value;
  }
  return null;
}

// Returns the enum member name for a number or a string in any letter case.
// An unknown value is returned as text.
String _enumName(List<String> names, Object? value) {
  if (value is int) {
    return value >= 0 && value < names.length ? names[value] : '$value';
  }
  if (value is double) {
    final whole = value.isFinite && value == value.truncateToDouble();
    return whole && value >= 0 && value < names.length
        ? names[value.toInt()]
        : '$value';
  }
  if (value is String) {
    final lower = value.toLowerCase();
    for (final name in names) {
      if (name.toLowerCase() == lower) return name;
    }
    return value;
  }
  return 'Unknown';
}

Map<String, Object?>? _parseJsonObject(String body) {
  if (body.isEmpty) return null;
  try {
    final value = jsonDecode(body);
    return value is Map<String, Object?> ? value : null;
  } on FormatException {
    return null;
  }
}

Duration? _seconds(Object? value, {required bool allowZero}) {
  if (value is! num || !value.isFinite) return null;
  if (value < 0 || (!allowZero && value == 0)) return null;
  return Duration(
      microseconds: (value * Duration.microsecondsPerSecond).round());
}

// Converts a v2 TokenDTO into the session shape, or returns null when it is
// not an ordinary session. Expiry times come from the lifetimes and this
// device's clock at receipt, not from the token's own exp claim, so a device
// clock that is wrong by a constant amount does not change when the client
// refreshes.
SessionCredentials? _sessionFromTokenDto(Object? token, DateTime receivedAt) {
  if (token is! Map) return null;
  final accessToken = _field(token, 'token');
  final refreshToken = _field(token, 'refreshToken');
  if (accessToken is! String ||
      accessToken.isEmpty ||
      refreshToken is! String ||
      refreshToken.isEmpty) {
    return null;
  }
  final accessLifetime =
      _seconds(_field(token, 'tokenLifeTimeInSeconds'), allowZero: false);
  if (accessLifetime == null) return null;
  // Flow is AuthPurpose. Only None (0) is an ordinary session. The Blazor
  // client checks the same.
  final flow = _field(token, 'flow');
  if (flow != null &&
      !(flow is num && flow == 0) &&
      !(flow is String && flow.toLowerCase() == 'none')) {
    return null;
  }
  final refreshLifetime =
      _seconds(_field(token, 'refreshTokenLifeTimeInSeconds'), allowZero: true);
  final at = receivedAt.toUtc();
  return SessionCredentials(
    accessToken: accessToken,
    accessExpiresAt: at.add(accessLifetime),
    refreshToken: refreshToken,
    refreshExpiresAt: refreshLifetime == null ? null : at.add(refreshLifetime),
    // userData is passed on as the host wrote it, so its property names
    // follow the host's letter case.
    extra: <String, Object?>{'userData': _field(token, 'userData')},
  );
}

// Reads a Retry-After header in seconds. The HTTP date form is ignored.
IssuerUnavailable _unavailable(TransportResponse response) {
  final header = response.header('Retry-After');
  final seconds = header == null ? null : num.tryParse(header.trim());
  return IssuerUnavailable(retryAfter: _seconds(seconds, allowZero: true));
}
