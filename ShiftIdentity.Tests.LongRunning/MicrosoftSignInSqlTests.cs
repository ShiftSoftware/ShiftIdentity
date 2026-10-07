using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Models;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>Sign in with Microsoft through the real routes and SQL store: every case of <see cref="ProviderSignInSqlTests"/>.</summary>
[Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class MicrosoftSignInSqlTests(SqlIdentityFixture fixture) : ProviderSignInSqlTests(fixture), IClassFixture<SqlIdentityFixture>
{
    private static readonly string Tenant = Guid.NewGuid().ToString("D");
    private static readonly string Person = Guid.NewGuid().ToString("D");

    protected override SignInProvider Provider => SignInProvider.Microsoft;
    private protected override ProviderIdentity DefaultIdentity => new(Tenant, Person, "provider.person@EXAMPLE.invalid", true, false);

    protected override void Enable(bool requireShiftMfa = false) => Fixture.Microsoft = new MicrosoftSignIn(
        new MicrosoftSignInSettings { Enabled = true, ClientId = Guid.NewGuid().ToString(), ClientSecret = "test", RequireShiftMfa = requireShiftMfa },
        "https://identity.example.invalid", Tokens);

    protected override void Disable() => Fixture.Microsoft = null;
}
