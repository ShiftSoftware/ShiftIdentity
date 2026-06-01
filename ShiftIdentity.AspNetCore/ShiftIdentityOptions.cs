using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.AspNetCore;

public class ShiftIdentityOptions
{
    public TokenUserDataDTO UserData { get; private set; }

    public string[] AccessTrees { get; private set; }

    public string? UserPassword { get; private set; }

    public ShiftIdentityConfiguration Configuration { get;private set; }

    public ShiftIdentityOptions(TokenUserDataDTO userData,
        string[] accessTrees,
        ShiftIdentityConfiguration configuration,
        string? userPassword = null)
    {
        this.UserData = userData;
        this.AccessTrees = accessTrees;
        this.Configuration = configuration;
        this.UserPassword = userPassword;
    }
}
