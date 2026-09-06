using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Enums;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

/// <summary>
/// Explicitly constructed by the isolated admission harness; not registered by AddShiftIdentity.
/// A challenge and its verifier live only for this component flow, never in IIdentityStore.
/// </summary>
public sealed class AuthenticationFlow(HttpClient http, IIdentityStore store, TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private string? verifier;
    private long generation;
    public AuthenticationChallenge? Pending { get; private set; }
    public bool Busy { get; private set; }

    public void Restart()
    {
        generation++;
        Pending = null;
        verifier = null;
    }

    public Task<AuthOutcome> LoginAsync(string username, string password) => SendAsync(() =>
    {
        Restart();
        verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return new HttpRequestMessage(HttpMethod.Post, "api/identity/v2/login")
        {
            Content = JsonContent.Create(new PasswordLoginRequest(username, password,
                Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))))
        };
    });

    public Task<AuthOutcome> CompleteMfaAsync(string code)
    {
        if (Pending is not { Step: AuthenticationStep.ExistingMfa, Handle: not null } challenge ||
            verifier is null || challenge.ExpiresAt <= clock.GetUtcNow())
        {
            Restart();
            return Task.FromResult<AuthOutcome>(new AuthenticationRefused(AuthenticationFailure.Expired));
        }
        return SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "api/identity/v2/login/mfa")
            {
                Content = JsonContent.Create(new CompleteMfaRequest(code, verifier))
            };
            request.Headers.Authorization = new("Operation", challenge.Handle);
            return request;
        });
    }

    private async Task<AuthOutcome> SendAsync(Func<HttpRequestMessage> create)
    {
        if (Busy) return new AuthenticationRefused(AuthenticationFailure.InvalidRequest);
        Busy = true;
        var requestGeneration = generation;
        try
        {
            using var request = create();
            requestGeneration = generation;
            using var response = await http.SendAsync(request);
            var outcome = await response.Content.ReadFromJsonAsync<AuthOutcome>();
            if (requestGeneration != generation)
                return new AuthenticationRefused(AuthenticationFailure.StaleOperation);
            switch (outcome)
            {
                case SessionIssued session when response.IsSuccessStatusCode && IsSession(session.Session):
                    Restart();
                    // No await between the generation check and initiating the one storage write.
                    await store.StoreTokenAsync(session.Session);
                    return session;
                case ChallengeRequired restricted when response.IsSuccessStatusCode &&
                    restricted.Challenge is { } challenge && Enum.IsDefined(challenge.Step) &&
                    challenge.ExpiresAt > clock.GetUtcNow() &&
                    (challenge.Step != AuthenticationStep.ExistingMfa || !string.IsNullOrWhiteSpace(challenge.Handle)):
                    Pending = challenge;
                    if (challenge.Step != AuthenticationStep.ExistingMfa) verifier = null;
                    return restricted;
                case AuthenticationRefused refused:
                    // A wrong TOTP may be retried within this same operation. All other errors require restart.
                    if (refused.Code != AuthenticationFailure.InvalidProof) Restart();
                    return refused;
                default:
                    Restart();
                    return new AuthenticationRefused(AuthenticationFailure.InvalidGrant);
            }
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException)
        {
            if (requestGeneration == generation) Restart();
            return new AuthenticationRefused(AuthenticationFailure.Unavailable);
        }
        finally { Busy = false; }
    }

    // Structural separation, not signature validation: the API authenticates the response over HTTPS.
    private bool IsSession(TokenDTO? session)
    {
        if (session is null || session.Flow != AuthPurpose.None || string.IsNullOrWhiteSpace(session.RefreshToken) ||
            session.TokenLifeTimeInSeconds is not (> 0 and <= 900)) return false;
        try
        {
            var token = new JsonWebToken(session.Token);
            return token.GetClaim("shift_purpose").Value == "access" &&
                token.GetClaim("shift_schema").Value == "2" &&
                token.ValidTo > clock.GetUtcNow().UtcDateTime;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return false; }
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
