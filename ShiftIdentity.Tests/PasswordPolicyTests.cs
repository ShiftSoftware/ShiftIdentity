using System.Buffers.Binary;
using System.Text;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Policy")]
public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("short", PasswordPolicyFailure.TooShort)]
    [InlineData("passwordpassword", PasswordPolicyFailure.Blocked)]
    [InlineData("synthetic-person123!", PasswordPolicyFailure.Blocked)]
    [InlineData("aaaaaaaaaaaaaaaa", PasswordPolicyFailure.Blocked)]
    [InlineData("a long password\nwith newline", PasswordPolicyFailure.InvalidText)]
    [InlineData("a sensible phrase in Unicode 茶", null)]
    public void New_password_policy_has_length_and_whole_value_blocking(string value, PasswordPolicyFailure? expected) =>
        Assert.Equal(expected, new NewPasswordPolicy().Validate(value, "synthetic-person"));

    [Fact]
    public void Policy_counts_normalized_unicode_codepoints_and_accepts_spaces_without_composition_rules()
    {
        var policy = new NewPasswordPolicy(["a newly blocked long password"]);
        Assert.Null(policy.Validate(string.Concat(Enumerable.Repeat("茶😀", 64)), "user"));
        Assert.Equal(PasswordPolicyFailure.TooLong, policy.Validate(string.Concat(Enumerable.Repeat("茶😀", 65)), "user"));
        Assert.Equal(PasswordPolicyFailure.TooShort, policy.Validate(string.Concat(Enumerable.Repeat("e\u0301", 14)), "user"));
        Assert.Equal(PasswordPolicyFailure.Blocked, policy.Validate("a newly blocked long password", "user"));
        Assert.Null(policy.Validate("password inside a unique longer sentence", "user"));
        Assert.Equal(PasswordPolicyFailure.InvalidText, policy.Validate("long password with \ud800", "user"));
    }

    [Fact]
    public void Adaptive_format_is_salted_versioned_normalized_and_verifies_legacy_without_changing_its_semantics()
    {
        const string value = "a long cafe\u0301 password";
        var a = HashService.GenerateVersionedHash(value);
        var b = HashService.GenerateVersionedHash(value);
        Assert.NotEqual(a.Salt, b.Salt);
        Assert.NotEqual(a.PasswordHash, b.PasswordHash);
        Assert.Equal(41, a.PasswordHash.Length);
        Assert.Equal(600_000, BinaryPrimitives.ReadInt32BigEndian(a.PasswordHash.AsSpan(5, 4)));
        Assert.False(VersionedPasswordHash.NeedsUpgrade(a.PasswordHash));
        Assert.True(HashService.VerifyVersionedPassword(value.Normalize(NormalizationForm.FormC), a.Salt, a.PasswordHash));
        Assert.False(HashService.VerifyVersionedPassword("a wrong long password", a.Salt, a.PasswordHash));
        var legacy = HashService.GenerateHash(value);
        Assert.True(VersionedPasswordHash.NeedsUpgrade(legacy.PasswordHash));
        Assert.True(HashService.VerifyVersionedPassword(value, legacy.Salt, legacy.PasswordHash));
        Assert.False(HashService.VerifyVersionedPassword(value.Normalize(NormalizationForm.FormC), legacy.Salt, legacy.PasswordHash));
    }

    [Fact]
    public void Unknown_malformed_and_excessive_cost_hashes_fail_closed()
    {
        var hash = HashService.GenerateVersionedHash("a long valid password");
        hash.PasswordHash[4] = 99;
        Assert.False(HashService.VerifyVersionedPassword("a long valid password", hash.Salt, hash.PasswordHash));
        hash.PasswordHash[4] = 1;
        BinaryPrimitives.WriteInt32BigEndian(hash.PasswordHash.AsSpan(5, 4), int.MaxValue);
        Assert.False(HashService.VerifyVersionedPassword("a long valid password", hash.Salt, hash.PasswordHash));
        Assert.False(HashService.VerifyVersionedPassword("a long valid password", [], []));
    }
}
