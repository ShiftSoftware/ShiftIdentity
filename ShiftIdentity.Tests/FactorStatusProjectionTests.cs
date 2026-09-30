using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Mappers;
using Xunit;

namespace ShiftIdentity.Tests;

public sealed class FactorStatusProjectionTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Current_security_state_wins_over_retained_plaintext(bool legacy, bool current, bool expected)
    {
        var user = new User
        {
            TotpSecret = legacy ? [1] : null,
            SecurityState = new UserSecurityState { ProtectedTotpSecret = current ? [2] : null }
        };
        Assert.Equal(expected, user.ToListDTO().TotpEnabled);
        Assert.Equal(expected, user.ToInfoDTO().TotpEnabled);
    }

    [Fact]
    public void Before_expansion_the_old_factor_remains_visible()
    {
        Assert.True(new User { TotpSecret = [1] }.ToListDTO().TotpEnabled);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void A_reset_account_is_listed_as_waiting_for_recovery_even_with_a_retained_plaintext_copy(bool recovery, bool expected)
    {
        // A reset leaves the plaintext column as it was (the recovery flag stops the startup copy from restoring it).
        var user = new User { TotpSecret = [1], SecurityState = new UserSecurityState { LocalMfaRecoveryRequired = recovery } };
        var list = user.ToListDTO();
        Assert.Equal(expected, list.MfaRecoveryRequired);
        Assert.False(list.TotpEnabled);
        Assert.False(new User { TotpSecret = [1] }.ToListDTO().MfaRecoveryRequired);
    }
}
