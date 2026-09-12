using System.Security.Cryptography;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Policy")]
public sealed class IdentityMaterialProtectionTests
{
    internal static FactorProtectionSettings Settings(string id = "key-1") => new()
    {
        ActiveKeyId = id, Keys = new() { [id] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }
    };

    [Fact]
    public void Independent_instances_use_configured_keys_and_random_nonces()
    {
        var settings = Settings();
        var first = new IdentityMaterialProtector(settings).CreateProtector("factor");
        var second = new IdentityMaterialProtector(settings).CreateProtector("factor");
        var secret = RandomNumberGenerator.GetBytes(20);
        var a = first.Protect(secret);
        var b = first.Protect(secret);
        Assert.NotEqual(a, b);
        Assert.Equal(secret, second.Unprotect(a));
        Assert.Equal(secret, second.Unprotect(b));
        Assert.Contains("key-1", System.Text.Encoding.ASCII.GetString(a));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("short")]
    [InlineData("base64")]
    [InlineData("identifier")]
    [InlineData("old-key")]
    public void Invalid_configuration_is_refused_without_printing_key_values(string scenario)
    {
        var settings = Settings();
        switch (scenario)
        {
            case "missing": settings = new(); break;
            case "unknown": settings.ActiveKeyId = "absent"; break;
            case "short": settings.Keys["key-1"] = Convert.ToBase64String(new byte[16]); break;
            case "base64": settings.Keys["key-1"] = "synthetic-invalid-key!"; break;
            case "identifier": settings.Keys[settings.ActiveKeyId = "bad:id"] = settings.Keys["key-1"]; break;
            case "old-key": settings.Keys["old"] = "synthetic-invalid-key!"; break;
        }
        var error = Assert.Throws<InvalidOperationException>(() => new IdentityMaterialProtector(settings));
        Assert.DoesNotContain("synthetic-invalid-key", error.ToString());
    }

    [Theory]
    [InlineData("header")]
    [InlineData("key-id")]
    [InlineData("nonce")]
    [InlineData("tag")]
    [InlineData("payload")]
    [InlineData("truncated")]
    [InlineData("wrong-key")]
    public void Tampered_or_unreadable_ciphertext_is_refused(string scenario)
    {
        var settings = Settings();
        var protector = new IdentityMaterialProtector(settings);
        var encrypted = protector.Protect(RandomNumberGenerator.GetBytes(20));
        var header = 5 + encrypted[4];
        switch (scenario)
        {
            case "header": encrypted[0] ^= 1; break;
            case "key-id": encrypted[5] ^= 1; break;
            case "nonce": encrypted[header] ^= 1; break;
            case "tag": encrypted[header + 12] ^= 1; break;
            case "payload": encrypted[^1] ^= 1; break;
            case "truncated": encrypted = encrypted[..10]; break;
            case "wrong-key": protector = new IdentityMaterialProtector(Settings()); break;
        }
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(encrypted));
    }

    [Fact]
    public void Purpose_nesting_and_factor_owner_and_generation_cannot_be_substituted()
    {
        var protector = new IdentityMaterialProtector(Settings());
        var state = new UserSecurityState { UserID = 7, FactorGeneration = 3 };
        MfaMaterial.ProtectActive(protector, state, RandomNumberGenerator.GetBytes(20));
        state.UserID++;
        Assert.ThrowsAny<CryptographicException>(() => MfaMaterial.ReadActive(protector, state));
        state.UserID--; state.FactorGeneration++;
        Assert.ThrowsAny<CryptographicException>(() => MfaMaterial.ReadActive(protector, state));
        var page = protector.CreateProtector("SecurityLinks.v1").CreateProtector("Page").Protect("synthetic-link");
        Assert.ThrowsAny<CryptographicException>(() => protector.CreateProtector("SecurityLinks.v1Page").Unprotect(page));
        Assert.ThrowsAny<CryptographicException>(() => protector.CreateProtector("factor").Unprotect(page));
    }

    [Fact]
    public void Rotation_reads_old_factors_and_links_and_reencrypts_with_the_active_key()
    {
        var settings = Settings("old");
        var old = new IdentityMaterialProtector(settings);
        var factor = old.Protect(new byte[] { 1, 2, 3 });
        var page = old.CreateProtector("SecurityLinks.v1").CreateProtector("Page").Protect("synthetic-link");
        settings.Keys["new"] = Settings().Keys["key-1"];
        settings.ActiveKeyId = "new";
        var current = new IdentityMaterialProtector(settings);
        Assert.Equal("synthetic-link", current.CreateProtector("SecurityLinks.v1").CreateProtector("Page").Unprotect(page));
        var replaced = current.Protect(current.Unprotect(factor));
        settings.Keys.Remove("old");
        var retired = new IdentityMaterialProtector(settings);
        Assert.Equal(new byte[] { 1, 2, 3 }, retired.Unprotect(replaced));
        Assert.ThrowsAny<CryptographicException>(() => retired.Unprotect(factor));
        // Configuration edits do not mutate an already running instance's keys.
        Assert.Equal(new byte[] { 1, 2, 3 }, current.Unprotect(factor));
    }
}
