# ShiftIdentity.TokenClient for Dart and Flutter

Two pub.dev packages, released together:

| Package | Contents |
|---|---|
| [`shift_identity_token_client`](shift_identity_token_client/) | Pure Dart. `TokenClient`, the ShiftIdentity v2 adapter, `TokenHttpClient`, the in-memory and JSON stores, and the store, adapter and transport contracts. Depends only on `http`. |
| [`shift_identity_token_client_flutter`](shift_identity_token_client_flutter/) | `SecureCredentialStore` on `flutter_secure_storage`, re-exports the core, and an example app. |

The behaviour matches the JavaScript client in [`../javascript`](../javascript/): the same outcomes, skew, single
flight, back-off, generation counter and v2 mapping, and the same stored JSON shape. The Dart client adds
`TokenHttpClient` and `TokenClient.replaceSession`.

## Develop

Each package has a `pubspec_overrides.yaml` that points the Flutter package (and its example) at the core package
in this folder, so changes are picked up without publishing. `dart pub publish` ignores those files.

```bash
cd shift_identity_token_client
dart pub get
dart analyze
dart test            # includes drift checks against ShiftIdentity.Core and the JS fixtures

cd ../shift_identity_token_client_flutter
flutter pub get
flutter analyze
flutter test

cd example
flutter pub get
flutter analyze
```

`test/fixtures/v2-refresh-responses.json` is a copy of the JavaScript client's fixture file. A test fails when the
two differ. The v2 tests also read `AuthOutcome.cs`, `TokenDTO.cs`, `AuthPurpose.cs` and `AdmissionEndpoints.cs`.
When `AuthenticationFailure` or `AuthenticationStep` gains a member, add it to `ShiftIdentityV2Issuer.failures` or
`steps` in the same position, as in the JavaScript adapter.

Format with `dart format .` (default width). pub.dev scores formatting.

## Release

Both packages carry the same version. The Flutter package depends on `shift_identity_token_client: ^<major>.0.0`,
so publish the core package first.

1. Bump `version` in both `pubspec.yaml` files, and add a `CHANGELOG.md` entry in each.
2. Run the checks above, then `dart pub publish --dry-run` in each package. Both must report 0 warnings.
3. `dart pub publish` in `shift_identity_token_client`, then in `shift_identity_token_client_flutter`.
4. On pub.dev, move both packages to the organisation's verified publisher (once, after the first release).
