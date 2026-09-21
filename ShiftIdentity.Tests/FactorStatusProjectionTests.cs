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
}
