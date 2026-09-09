using System.Net.Http.Json;
using System.Security.Claims;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Enums;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

/// <summary>Opt-in v2 session storage. Never stores restricted credentials or renews through legacy issuance.</summary>
public sealed class AdmissionSessionStore(HttpClient http, ISyncLocalStorageService storage, string storageKey)
    : AuthenticationStateProvider, IIdentityStore
{
    private Task<bool>? renewal;
    private long generation;
    public string? GetToken() => Read() is { } token && IsSession(token) ? token.Token : null;
    public async Task<TokenDTO?> GetTokenAsync()
    {
        var token = Read();
        if (token is null) return null;
        if (!IsSession(token, 10)) await RenewAsync();
        token = Read();
        return token is not null && IsSession(token) ? token : null;
    }

    public Task StoreTokenAsync(TokenDTO token)
    {
        if (!IsSession(token)) throw new ArgumentException("Only a current ordinary v2 session can be stored.", nameof(token));
        generation++;
        storage.SetItem(storageKey, token);
        Notify();
        return Task.CompletedTask;
    }

    public Task RemoveTokenAsync()
    {
        generation++;
        storage.RemoveItem(storageKey);
        Notify();
        return Task.CompletedTask;
    }

    public async Task<bool> RenewAsync()
    {
        if (renewal is not null) return await renewal;
        var pending = RenewCoreAsync();
        renewal = pending;
        try { return await pending; }
        finally { if (ReferenceEquals(renewal, pending)) renewal = null; }
    }

    private async Task<bool> RenewCoreAsync()
    {
        var before = Read();
        if (before is null) return false;
        var started = generation;
        try
        {
            using var response = await http.PostAsJsonAsync("api/identity/v2/refresh", new RenewSessionRequest(before.RefreshToken));
            var result = await response.Content.ReadFromJsonAsync<AuthOutcome>();
            // A renewal started before logout, a new login or a credential change cannot replace its result.
            // Comparing storage also notices updates already made by another tab.
            if (generation != started || Read()?.Token != before.Token) return GetToken() is not null;
            if (response.IsSuccessStatusCode && result is SessionIssued session && IsSession(session.Session))
            {
                await StoreTokenAsync(session.Session);
                return true;
            }
            if (result is AuthenticationRefused { Code: not AuthenticationFailure.Unavailable })
                await RemoveTokenAsync();
            return false;
        }
        catch (Exception error) when (error is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        { return false; }
    }

    private TokenDTO? Read()
    {
        try { return storage.GetItem<TokenDTO>(storageKey); }
        catch (System.Text.Json.JsonException) { storage.RemoveItem(storageKey); return null; }
    }

    // Structural validation only. The API verifies signatures, client context and current authority.
    public static bool IsSession(TokenDTO? value, int secondsRemaining = 0)
    {
        if (value is null || value.Flow != AuthPurpose.None || string.IsNullOrWhiteSpace(value.RefreshToken) ||
            value.TokenLifeTimeInSeconds is not (> 0 and <= 900)) return false;
        try
        {
            var token = new JsonWebToken(value.Token);
            return token.GetClaim("shift_schema").Value == "2" && token.GetClaim("shift_purpose").Value == "access" &&
                token.ValidTo > DateTime.UtcNow.AddSeconds(secondsRemaining);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return false; }
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = GetToken();
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(token is null ? new ClaimsIdentity() :
            new ClaimsIdentity(new JsonWebToken(token).Claims, "jwt"))));
    }
    private void Notify() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}
