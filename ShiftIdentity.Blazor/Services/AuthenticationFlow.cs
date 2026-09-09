using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Enums;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

/// <summary>Opt-in staged flow. Restricted credentials live only in this component's memory.</summary>
public sealed partial class AuthenticationFlow(HttpClient http, IIdentityStore store, TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private string? verifier;
    private long generation;
    public AuthenticationChallenge? Pending { get; private set; }
    public bool Busy { get; private set; }
    public bool PasswordWasChanged { get; private set; }
    public bool MfaWasChanged { get; private set; }
    public bool ReturnToLoginRequired { get; private set; }

    // Invalidates late responses locally. Visible cancel/restart controls also call CancelAsync.
    public void Restart() { generation++; Pending = null; verifier = null; }

    public Task<AuthOutcome> LoginAsync(string username, string password) => SendAsync(() =>
    {
        Start();
        return Request("login", new PasswordLoginRequest(username, password, Challenge()));
    });

    public Task<AuthOutcome> BeginPasswordChangeAsync(string access) => SendAsync(() =>
    {
        Start();
        var request = Request("password-change", new StartPasswordChangeRequest(Challenge()));
        request.Headers.Authorization = new("Bearer", access);
        return request;
    });

    public Task<AuthOutcome> ProvePasswordAsync(string password) => Continue(AuthenticationStep.Password,
        Pending?.Purpose == AuthenticationOperationPurpose.MfaEnrollment ? "mfa/password" : "password-change/password",
        proof => new PasswordChangeProofRequest(password, proof));

    public Task<AuthOutcome> ChangePasswordAsync(string password) => Continue(AuthenticationStep.PasswordChange,
        "password-change/complete", proof => new CompletePasswordChangeRequest(password, proof));

    public Task<AuthOutcome> CompleteMfaAsync(string code) => Continue(AuthenticationStep.ExistingMfa,
        Pending?.Purpose switch { AuthenticationOperationPurpose.PasswordChange => "password-change/mfa",
            AuthenticationOperationPurpose.MfaReplacement => "mfa/existing", _ => "login/mfa" },
        proof => new CompleteMfaRequest(code, proof));

    public Task<AuthOutcome> BeginMfaAsync(string access, bool replace = false) => SendAsync(() =>
    {
        Start();
        var request = Request("mfa/start", new StartMfaRequest(Challenge(), replace));
        request.Headers.Authorization = new("Bearer", access);
        return request;
    });

    public Task<AuthOutcome> ConfirmNewFactorAsync(string code) => Continue(AuthenticationStep.NewMfa,
        "mfa/confirm", proof => new CompleteMfaRequest(code, proof));

    public Task<AuthOutcome> RecoverMfaAsync(string username, string password, string recoveryCode) => SendAsync(() =>
    {
        Start();
        return Request("mfa/recover", new RecoverMfaRequest(username, password, recoveryCode, Challenge()));
    });

    public Task<AuthOutcome> IssueMfaRecoveryAsync(string access, long userID, string verificationReference) => SendAsync(() =>
    {
        var request = Request("mfa/recovery-code", new IssueMfaRecoveryRequest(userID, verificationReference));
        request.Headers.Authorization = new("Bearer", access);
        return request;
    });

    public async Task<AuthOutcome> CancelAsync()
    {
        var pending = Pending;
        var proof = verifier;
        Restart();
        return pending?.Handle is { } handle && proof is not null
            ? await CancelCredentialAsync(handle, proof) : new OperationCancelled();
    }

    private void Start()
    {
        Restart(); PasswordWasChanged = false; MfaWasChanged = false; ReturnToLoginRequired = false;
        verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
    }
    private string Challenge() => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier!)));

    private Task<AuthOutcome> Continue<T>(AuthenticationStep step, string route, Func<string, T> body)
    {
        if (Pending is not { Handle: not null } challenge || challenge.Step != step ||
            verifier is null || challenge.ExpiresAt <= clock.GetUtcNow())
        {
            Restart();
            return Task.FromResult<AuthOutcome>(new AuthenticationRefused(AuthenticationFailure.Expired));
        }
        var proof = verifier;
        return SendAsync(() =>
        {
            var request = Request(route, body(proof));
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
            var requestVerifier = verifier;
            using var response = await http.SendAsync(request);
            var outcome = await response.Content.ReadFromJsonAsync<AuthOutcome>();
            if (requestGeneration != generation)
            {
                // If cancellation raced an intermediate transition, revoke its newly rotated handle too.
                if (outcome is ChallengeRequired { Challenge.Handle: { } handle } && requestVerifier is not null)
                    await CancelCredentialAsync(handle, requestVerifier);
                return new AuthenticationRefused(AuthenticationFailure.StaleOperation);
            }
            var changed = outcome as PasswordChanged;
            if (changed is not null && response.IsSuccessStatusCode)
            {
                PasswordWasChanged = true;
                outcome = changed.Continuation;
            }
            var mfaChanged = outcome as MfaChanged;
            if (mfaChanged is not null && response.IsSuccessStatusCode)
            {
                MfaWasChanged = true;
                PasswordWasChanged |= mfaChanged.PasswordAlsoChanged;
                outcome = mfaChanged.Continuation;
            }
            AuthOutcome Completed(AuthOutcome value) => mfaChanged is not null ? new MfaChanged(value, mfaChanged.PasswordAlsoChanged)
                : changed is not null ? new PasswordChanged(value) : value;
            switch (outcome)
            {
                case SessionIssued session when response.IsSuccessStatusCode && IsSession(session.Session):
                    Restart();
                    await store.StoreTokenAsync(session.Session);
                    return Completed(session);
                case ChallengeRequired restricted when response.IsSuccessStatusCode &&
                    restricted.Challenge is { } challenge && Enum.IsDefined(challenge.Step) && Enum.IsDefined(challenge.Purpose) &&
                    challenge.ExpiresAt > clock.GetUtcNow() &&
                    (challenge.Step != AuthenticationStep.ExistingMfa || !string.IsNullOrWhiteSpace(challenge.Handle)):
                    Pending = challenge;
                    if (challenge.Handle is null) verifier = null;
                    return Completed(restricted);
                case ReturnToLogin when response.IsSuccessStatusCode && mfaChanged is not null:
                    Restart(); ReturnToLoginRequired = true;
                    await store.RemoveTokenAsync();
                    return Completed(new ReturnToLogin());
                case MfaRecoveryCodeIssued issued when response.IsSuccessStatusCode:
                    return issued;
                case AuthenticationRefused refused:
                    // Invalid proof and password-policy errors permit correction within the original deadline.
                    if (refused.Code is not (AuthenticationFailure.InvalidProof or AuthenticationFailure.InvalidNewPassword)) Restart();
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

    private async Task<AuthOutcome> CancelCredentialAsync(string handle, string proof)
    {
        try
        {
            using var request = Request("operations/cancel", new CancelOperationRequest(proof));
            request.Headers.Authorization = new("Operation", handle);
            using var response = await http.SendAsync(request);
            return await response.Content.ReadFromJsonAsync<AuthOutcome>() ?? new AuthenticationRefused(AuthenticationFailure.Unavailable);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException)
        { return new AuthenticationRefused(AuthenticationFailure.Unavailable); }
    }

    private static HttpRequestMessage Request<T>(string route, T body) =>
        new(HttpMethod.Post, "api/identity/v2/" + route) { Content = JsonContent.Create(body) };

    // Structural separation, not signature validation: the API authenticates the response over HTTPS.
    private bool IsSession(TokenDTO? session)
    {
        if (session is null || session.Flow != AuthPurpose.None || string.IsNullOrWhiteSpace(session.RefreshToken) ||
            session.TokenLifeTimeInSeconds is not (> 0 and <= 900)) return false;
        try
        {
            var token = new JsonWebToken(session.Token);
            return token.GetClaim("shift_purpose").Value == "access" &&
                token.GetClaim("shift_schema").Value == "2" && token.ValidTo > clock.GetUtcNow().UtcDateTime;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return false; }
    }
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
