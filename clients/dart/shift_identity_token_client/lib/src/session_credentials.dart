/// The session that a `TokenClient` keeps in its `CredentialStore`.
///
/// Expiry times are absolute, computed with this device's clock when the
/// session arrives, and kept in UTC. `refreshExpiresAt` may be `null`, which
/// means unknown: the issuer then decides whether the refresh token is still
/// valid. [extra] belongs to the issuer adapter (the ShiftIdentity v2 adapter
/// puts `{'userData': ...}` there) and must be JSON-encodable when the store
/// serializes the session.
final class SessionCredentials {
  /// Creates a session. A stored session needs at least an [accessToken] or a
  /// [refreshToken]; a session that an issuer returns needs [accessToken],
  /// [accessExpiresAt] and [refreshToken].
  const SessionCredentials({
    this.accessToken,
    this.accessExpiresAt,
    this.refreshToken,
    this.refreshExpiresAt,
    this.extra,
  });

  /// The access token sent as `Authorization: Bearer <accessToken>`.
  final String? accessToken;

  /// When [accessToken] expires.
  final DateTime? accessExpiresAt;

  /// The token the issuer exchanges for a new session.
  final String? refreshToken;

  /// When [refreshToken] expires, or `null` when unknown.
  final DateTime? refreshExpiresAt;

  /// Issuer-specific data, passed through to `TokenAvailable.extra`.
  final Object? extra;

  /// Whether the session has an access token, a refresh token, or both.
  /// The client treats any other stored value as no session.
  bool get isReadable => _hasText(accessToken) || _hasText(refreshToken);

  /// Whether the session has everything a fresh session from an issuer must
  /// have: an access token with its expiry, and a refresh token.
  bool get isComplete =>
      _hasText(accessToken) &&
      accessExpiresAt != null &&
      _hasText(refreshToken);

  /// Returns a copy with the given fields replaced.
  SessionCredentials copyWith({
    String? accessToken,
    DateTime? accessExpiresAt,
    String? refreshToken,
    DateTime? refreshExpiresAt,
    Object? extra,
  }) =>
      SessionCredentials(
        accessToken: accessToken ?? this.accessToken,
        accessExpiresAt: accessExpiresAt ?? this.accessExpiresAt,
        refreshToken: refreshToken ?? this.refreshToken,
        refreshExpiresAt: refreshExpiresAt ?? this.refreshExpiresAt,
        extra: extra ?? this.extra,
      );

  /// The JSON shape, with times as milliseconds since 1970 (UTC).
  ///
  /// It is the same shape the JavaScript client keeps in `localStorage`, so a
  /// Flutter web app and a JavaScript page can share one stored session.
  Map<String, Object?> toJson() => <String, Object?>{
        'accessToken': accessToken,
        'accessExpiresAt': accessExpiresAt?.millisecondsSinceEpoch,
        'refreshToken': refreshToken,
        'refreshExpiresAt': refreshExpiresAt?.millisecondsSinceEpoch,
        'extra': extra,
      };

  /// Reads the shape that [toJson] writes. Returns `null` for anything that is
  /// not a readable session (not a map, or no token at all). A field with the
  /// wrong type is read as missing.
  static SessionCredentials? tryFromJson(Object? json) {
    if (json is! Map) return null;
    final session = SessionCredentials(
      accessToken: _text(json['accessToken']),
      accessExpiresAt: _time(json['accessExpiresAt']),
      refreshToken: _text(json['refreshToken']),
      refreshExpiresAt: _time(json['refreshExpiresAt']),
      extra: json['extra'],
    );
    return session.isReadable ? session : null;
  }

  /// Tokens are never printed, so a session can be logged safely.
  @override
  String toString() => 'SessionCredentials('
      'accessToken: ${_hasText(accessToken) ? '<redacted>' : 'none'}, '
      'accessExpiresAt: $accessExpiresAt, '
      'refreshToken: ${_hasText(refreshToken) ? '<redacted>' : 'none'}, '
      'refreshExpiresAt: $refreshExpiresAt)';
}

bool _hasText(String? value) => value != null && value.isNotEmpty;

String? _text(Object? value) =>
    value is String && value.isNotEmpty ? value : null;

DateTime? _time(Object? value) {
  if (value is! num || !value.isFinite) return null;
  return DateTime.fromMillisecondsSinceEpoch(value.round(), isUtc: true);
}
