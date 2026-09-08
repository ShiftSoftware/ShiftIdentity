using System.Security.Cryptography;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Policy")]
public sealed class MfaCredentialTests
{
    [Fact]
    public void Recovery_code_supports_grouped_paste_and_is_bound_to_its_key()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var credential = MfaRecoveryCredential.Create(key);
        Assert.Equal(26, credential.Code.Replace("-", "").Length);
        Assert.Equal(32, credential.Digest.Length);
        Assert.True(MfaRecoveryCredential.Verify(credential.Code, credential.Digest, key));
        Assert.True(MfaRecoveryCredential.Verify(credential.Code.ToLowerInvariant().Replace('-', ' '), credential.Digest, key));
        Assert.False(MfaRecoveryCredential.Verify(credential.Code, credential.Digest, RandomNumberGenerator.GetBytes(32)));
        Assert.False(MfaRecoveryCredential.Verify(MfaRecoveryCredential.Create(key).Code, credential.Digest, key));
        Assert.False(OperationCredential.TryRead(credential.Code, key, out _, out _));
        Assert.False(MfaRecoveryCredential.Verify(OperationCredential.Create(key).Handle, credential.Digest, key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("SHORT")]
    [InlineData("00000000000000000000000000")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAA!")]
    public void Malformed_recovery_codes_are_refused(string code)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        Assert.False(MfaRecoveryCredential.Verify(code, MfaRecoveryCredential.Create(key).Digest, key));
    }
}
