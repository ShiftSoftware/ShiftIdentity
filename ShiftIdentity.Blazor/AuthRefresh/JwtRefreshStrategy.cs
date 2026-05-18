using Microsoft.IdentityModel.JsonWebTokens;
using ShiftSoftware.ShiftIdentity.Blazor.Providers;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;

namespace ShiftSoftware.ShiftIdentity.Blazor.AuthRefresh;

/// <summary>
/// Standalone-WASM (JWT in localStorage) implementation of <see cref="IAuthRefreshStrategy"/>.
/// <para>
/// Interface members (used by the shared polling loop): <see cref="GetInitialClaims"/> reads
/// claims from the stored JWT; <see cref="RefreshAsync"/> POSTs the stored refresh token to
/// <c>/Auth/Refresh</c> and stores the rotated JWT.
/// </para>
/// <para>
/// JWT-only extras (called directly by JWT-mode UI — Dashboard <c>LoginForm</c> / <c>UserAvatar</c>):
/// <see cref="LoginAsync"/> POSTs credentials to <c>/Auth/Login</c> and stores the returned
/// <see cref="TokenDTO"/>; <see cref="ClearStoredTokenAsync"/> removes the stored token on logout.
/// These are not on <see cref="IAuthRefreshStrategy"/> because the cookie strategy has no
/// equivalent operation on the WASM side (cookie login + logout are form-post flows handled
/// server-side).
/// </para>
/// </summary>
public class JwtRefreshStrategy : IAuthRefreshStrategy
{
    private readonly IIdentityStore _store;
    private readonly IShiftIdentityProvider _identityProvider;
    private readonly ShiftIdentityBlazorOptions _options;

    public JwtRefreshStrategy(
        IIdentityStore store,
        IShiftIdentityProvider identityProvider,
        ShiftIdentityBlazorOptions options)
    {
        _store = store;
        _identityProvider = identityProvider;
        _options = options;
    }

    public List<UserClaimModel>? GetInitialClaims()
        => ParseJwtClaims(_store.GetToken());

    public async Task<List<UserClaimModel>?> RefreshAsync()
    {
        var stored = await _store.GetTokenAsync();
        if (stored is null || string.IsNullOrEmpty(stored.RefreshToken))
            return null;

        var result = await _identityProvider.RefreshTokenAsync(_options.BaseUrl, new RefreshDTO { RefreshToken = stored.RefreshToken });
        if (!result.IsSuccess || result.Data?.Entity == null)
            return null;

        await _store.StoreTokenAsync(result.Data.Entity);
        return ParseJwtClaims(result.Data.Entity.Token);
    }

    public async Task<LoginResult> LoginAsync(LoginDTO dto)
    {
        var response = await _identityProvider.LoginAsync(_options.BaseUrl, dto);

        var token = response.Data?.Entity;
        if (!response.IsSuccess || token is null)
        {
            return LoginResult.Failure(
                response.Data?.Message?.Body ?? response.ErrorMessage,
                response.Data?.Message?.Title);
        }

        await _store.StoreTokenAsync(token);

        return new LoginResult
        {
            IsSuccess = true,
            RequirePasswordChange = token.RequirePasswordChange,
            Claims = ParseJwtClaims(token.Token),
        };
    }

    /// <summary>
    /// Forced-change completion. The currently stored token is the short-lived challenge
    /// issued at login; the TokenMessageHandler attaches it as the Bearer for this call.
    /// On success the challenge is replaced with the full session token returned by the
    /// server, and the caller pushes the new claims to <c>ShiftAuthStateProvider</c>.
    /// </summary>
    public async Task<LoginResult> CompletePasswordChangeAsync(CompletePasswordChangeDTO dto)
    {
        var response = await _identityProvider.CompletePasswordChangeAsync(_options.BaseUrl, dto);

        var token = response.Data?.Entity;
        if (!response.IsSuccess || token is null)
        {
            return LoginResult.Failure(
                response.Data?.Message?.Body ?? response.ErrorMessage,
                response.Data?.Message?.Title);
        }

        await _store.StoreTokenAsync(token);

        return new LoginResult
        {
            IsSuccess = true,
            RequirePasswordChange = token.RequirePasswordChange,
            Claims = ParseJwtClaims(token.Token),
        };
    }

    public Task ClearStoredTokenAsync() => _store.RemoveTokenAsync();

    private static List<UserClaimModel>? ParseJwtClaims(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt)) return null;
        try
        {
            var token = new JsonWebTokenHandler().ReadToken(jwt) as JsonWebToken;
            return token?.Claims.Select(c => new UserClaimModel { Type = c.Type, Value = c.Value }).ToList();
        }
        catch
        {
            return null;
        }
    }
}
