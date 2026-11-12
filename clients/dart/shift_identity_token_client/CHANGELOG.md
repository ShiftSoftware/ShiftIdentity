## 1.0.0

First release.

- `TokenClient` with `acquireToken`, `replaceSession`, `clear` and `close`: refreshes ahead of expiry (skew), runs
  a single flight, backs off, and returns `TokenAvailable`, `SignInRequired` or `TemporarilyUnavailable`.
- `ShiftIdentityV2Issuer`: renews sessions with `POST /api/identity/v2/refresh`, and converts a v2 `TokenDTO` with
  `sessionFromToken`.
- `TokenHttpClient`: an `http.Client` that attaches the access token to requests for configured API base URLs.
- Stores: `InMemoryCredentialStore` and `JsonCredentialStore`.
- `HttpTokenTransport` on `package:http`, and the public `TokenIssuer` adapter contract (version 1) for other
  issuers.
