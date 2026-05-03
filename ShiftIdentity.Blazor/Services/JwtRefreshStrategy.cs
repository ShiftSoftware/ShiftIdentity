using Microsoft.IdentityModel.JsonWebTokens;
using ShiftSoftware.ShiftIdentity.Blazor.Providers;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

/// <summary>
/// Standalone-WASM (JWT in localStorage) implementation of <see cref="IAuthRefreshStrategy"/>.
/// Refreshes by POSTing the stored refresh token to the identity server's <c>/Auth/Refresh</c>
/// endpoint and replacing the stored <see cref="TokenDTO"/>.
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

    public async Task<List<UserClaimModel>?> OnLoginCommittedAsync(AuthLoginResult result)
    {
        if (result.Token is null) return null;
        await _store.StoreTokenAsync(result.Token);
        return ParseJwtClaims(result.Token.Token);
    }

    public Task OnLogoutAsync() => _store.RemoveTokenAsync();

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
