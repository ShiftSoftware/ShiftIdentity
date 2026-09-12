namespace ShiftSoftware.ShiftIdentity.Blazor;

[Obsolete("Delete your identity store class and its registration. Select RefreshTokenStorage.Cookie and CookieDomain in the AddShiftIdentity options, and inject IdentitySession wherever IIdentityStore was injected.", error: true)]
public interface IIdentityStore
{
}
