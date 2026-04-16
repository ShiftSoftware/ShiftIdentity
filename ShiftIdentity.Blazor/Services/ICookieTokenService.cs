namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

/// <summary>
/// Handles exchanging an auth code for a token and setting the auth cookie during SSR.
/// Implemented by the server project which has access to HttpContext.
/// </summary>
public interface ICookieTokenService
{
    string? GetCodeVerifierFromCookie();
    Task<bool> ExchangeCodeAndSignInAsync(string appId, Guid authCode, string codeVerifier);
}
