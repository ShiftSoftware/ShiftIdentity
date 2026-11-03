using ShiftSoftware.ShiftIdentity.Core.ValidatorsAndFormatters;
using Xunit;

namespace ShiftIdentity.Tests;

[Collection("PhoneNumberEngine")]
[Trait("Category", "Policy")]
public sealed class PhoneNumberEngineTests
{
    [Theory]
    [InlineData("+9647501234567", true)]
    [InlineData("abc", false)]
    public void Validates_when_the_engine_is_available(string phone, bool expected) =>
        Assert.Equal(expected, PhoneNumber.PhoneIsValid(phone));

    [Fact]
    public void Skips_the_check_while_the_engine_is_not_loaded()
    {
        PhoneNumber.EngineAvailable = false;

        try
        {
            Assert.True(PhoneNumber.PhoneIsValid("abc"));
        }
        finally
        {
            PhoneNumber.EngineAvailable = true;
        }
    }
}
