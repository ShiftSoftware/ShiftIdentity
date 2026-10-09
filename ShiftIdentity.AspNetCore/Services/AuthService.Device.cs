using System.Security.Cryptography;
using System.Text;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using static ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.AdmissionRules;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

/// <summary>
/// Device sign-in (the OAuth 2.0 Device Authorization Grant, RFC 8628) for devices that have only a remote control.
/// The device asks for a secret device code and a public user code. A person opens the code on a phone, sees which
/// device asks and which code it shows, and types the username and password of the account the device should use, or
/// denies it. The phone never signs in. Only an account that allows device sign-in can be used. The device polls with
/// its device code and receives an ordinary session of that account, bound to this host's own client, which renews
/// through v2/refresh. Every account change and the issue run under the same admission lock as login and refresh.
/// </summary>
public partial class AuthService
{
    private static readonly TimeSpan DeviceThrottleWindow = TimeSpan.FromMinutes(15);
    // RFC 8628 adds 5 seconds for every early poll. The interval stops growing here.
    private const int DeviceMaximumInterval = 60;

    internal static Task<AuthOutcome> StartDeviceAuthorizationAsync(IdentityAdmissionServices services,
        StartDeviceAuthorizationRequest request, string address, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        if (services.Device is not { } device || !device.Clients.ContainsKey(request.ClientId))
            return Refuse(AuthenticationFailure.ClientDenied);
        var now = services.Clock.GetUtcNow();
        if (!await services.Store.ConsumeIngressAsync(DeviceKey(services, "device-authorize:" + address), now,
            services.DeliveryLimits.DeviceAuthorizationsPerIpPer15Minutes, DeviceThrottleWindow, ct))
            return Refuse(AuthenticationFailure.AttemptsExhausted);
        // A user code must be unique among the rows that still hold one. A collision is rare; draw again.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var credential = OperationCredential.Create(services.Options.OperationKey);
            var userCode = NewUserCode();
            var stored = await services.Store.AddDeviceAuthorizationAsync(new DeviceAuthorization
            {
                ID = credential.ID, DeviceCodeDigest = credential.Digest, UserCodeDigest = UserCodeDigest(services, userCode),
                ClientID = request.ClientId, Audience = services.Client.Audience, State = DeviceAuthorizationState.Pending,
                CreatedAt = now, ExpiresAt = now.AddSeconds(device.LifetimeSeconds), Interval = device.IntervalSeconds
            }, ct);
            if (!stored) continue;
            var shown = DeviceUserCode.Format(userCode);
            return new DeviceAuthorizationStarted(credential.Handle, shown, device.VerificationUri,
                device.VerificationUri + "?code=" + shown, device.LifetimeSeconds, device.IntervalSeconds);
        }
        return Refuse(AuthenticationFailure.Unavailable);
    });

    /// <summary>
    /// The screen's poll. Every refusal carries its OAuth error code. Only a device code can poll: anything else,
    /// the user code included, is an unknown code, and unknown codes cost the caller's address.
    /// </summary>
    internal static async Task<AuthOutcome> PollDeviceAuthorizationAsync(IdentityAdmissionServices services,
        DeviceTokenRequest request, string address, CancellationToken ct)
    {
        try
        {
            if (!Valid(request)) return DeviceRefusal(AuthenticationFailure.InvalidRequest);
            if (services.Device is null) return DeviceRefusal(AuthenticationFailure.InvalidGrant);
            var now = services.Clock.GetUtcNow();
            DeviceAuthorization? found = null;
            var digest = Array.Empty<byte>();
            if (OperationCredential.TryRead(request.DeviceCode, services.Options.OperationKey, out var id, out digest))
            {
                found = await services.Store.ReadDeviceAuthorizationAsync(id, ct);
                if (found?.DeviceCodeDigest is not { } stored || !CryptographicOperations.FixedTimeEquals(stored, digest)) found = null;
            }
            if (found is null)
            {
                // A screen that holds its own code never comes here, so a legitimate device is never slowed by this.
                return await services.Store.ConsumeIngressAsync(DeviceKey(services, "device-token:" + address), services.Clock.GetUtcNow(),
                    services.DeliveryLimits.DeviceUnknownCodesPerIpPer15Minutes, DeviceThrottleWindow, ct)
                    ? DeviceRefusal(AuthenticationFailure.InvalidGrant) : DeviceRefusal(AuthenticationFailure.AttemptsExhausted);
            }
            if (found.State != DeviceAuthorizationState.Approved)
            {
                // Before approval no account is involved: the poll changes the row alone, under its lock. Null means
                // the row was approved in the meantime, and the poll continues to the issue below.
                var polled = await services.Store.AdmitDeviceAuthorizationAsync(found.ID, row => Poll(row, digest, now), ct);
                if (polled is not null) return polled;
            }
            services.Observe?.Invoke("DeviceProof");
            return await IssueDeviceSessionAsync(services, found.ID, digest, ct);
        }
        // A conflict or an unavailable store is temporary: the screen keeps its code and polls again.
        catch (Exception error) when (error is IdentitySecurityConflictException or IdentitySecurityUnavailableException
            or CryptographicException or OperationCanceledException)
        { return DeviceRefusal(AuthenticationFailure.Unavailable); }
    }

    private static AuthOutcome? Poll(DeviceAuthorization? row, byte[] digest, DateTimeOffset now)
    {
        if (row?.DeviceCodeDigest is not { } stored || !CryptographicOperations.FixedTimeEquals(stored, digest))
            return DeviceRefusal(AuthenticationFailure.InvalidGrant);
        switch (row.State)
        {
            case DeviceAuthorizationState.Approved:
                return null;
            case DeviceAuthorizationState.Denied:
                return DeviceRefusal(AuthenticationFailure.AccessDenied);
            case DeviceAuthorizationState.Pending when now >= row.ExpiresAt:
                ExpireDevice(row);
                return DeviceRefusal(AuthenticationFailure.ExpiredToken);
            case DeviceAuthorizationState.Pending when row.LastPolledAt is { } last && now < last.AddSeconds(row.Interval):
                row.Interval = Math.Min(row.Interval + 5, DeviceMaximumInterval);
                row.LastPolledAt = now;
                return DeviceRefusal(AuthenticationFailure.SlowDown);
            case DeviceAuthorizationState.Pending:
                row.LastPolledAt = now;
                return DeviceRefusal(AuthenticationFailure.AuthorizationPending);
            default:
                return DeviceRefusal(AuthenticationFailure.InvalidGrant);
        }
    }

    // The approved row names the account. The account is locked first and the row second, the same order as approval,
    // and both are checked again under the locks. One transaction issues the session and consumes the row.
    private static async Task<AuthOutcome> IssueDeviceSessionAsync(IdentityAdmissionServices services, Guid id, byte[] digest,
        CancellationToken ct)
    {
        var approved = await services.Store.ReadDeviceAuthorizationAsync(id, ct);
        if (approved?.UserID is not { } userID) return DeviceRefusal(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(userID, null, services.Client, async unit =>
        {
            var row = await services.Store.LockDeviceAuthorizationAsync(id, ct);
            if (row?.DeviceCodeDigest is not { } stored || !CryptographicOperations.FixedTimeEquals(stored, digest) ||
                row.State != DeviceAuthorizationState.Approved || row.UserID != unit.User.ID ||
                row.SecurityVersion is not { } version || row.PolicyRevision is not { } policy ||
                row.MfaSatisfied is not { } mfa || row.AuthenticatedAt is not { } authenticatedAt)
                return DeviceRefusal(AuthenticationFailure.InvalidGrant);
            var now = services.Clock.GetUtcNow();
            if (now >= row.ExpiresAt)
            {
                ExpireDevice(row);
                return DeviceRefusal(AuthenticationFailure.ExpiredToken);
            }
            if (row.Audience != services.Client.Audience || services.Device?.Clients.ContainsKey(row.ClientID) != true)
                return DeviceRefusal(AuthenticationFailure.ClientDenied);
            // The account is still active and on the version, policy and factors that were pinned at approval.
            var refusal = CommonRefusal(services, unit, version, policy);
            if (refusal is not null) return DeviceRefusal(refusal.Code);
            // An administrator can take device sign-in away between approval and the device's next poll.
            if (!unit.User.AllowDeviceSignIn) return DeviceRefusal(AuthenticationFailure.DeviceSignInNotAllowed);
            if (unit.Security.FactorGeneration != row.FactorGeneration) return DeviceRefusal(AuthenticationFailure.StaleOperation);
            if (ProviderRefusal(services, row.SessionProvider) is { } providerRefusal) return DeviceRefusal(providerRefusal.Code);
            // The device session owes no step, so it can renew. The account owed none at approval either.
            if (SessionStep(services, unit, row.SessionProvider, mfa) is not null) return DeviceRefusal(AuthenticationFailure.StaleOperation);
            // The session dates from the password typed on the phone, on this host's own client. The approval already
            // counted that password, so the failure counters stay as they are.
            var proof = new SessionProof(unit.User.ID, unit.Security.SecurityVersion, unit.Policy.Revision, unit.Security.FactorGeneration,
                mfa, authenticatedAt, services.Client.ID, services.Client.Audience, services.Client.External,
                services.HashIds.Encode<UserDTO>(unit.User.ID), Provider: row.SessionProvider);
            var issued = Issue(services, unit, proof, now, freshAuthentication: false);
            row.State = DeviceAuthorizationState.Consumed;
            row.DeviceCodeDigest = null;
            row.UserCodeDigest = null;
            unit.Audit("DeviceSessionIssued", now, row.ID);
            return issued;
        }, ct);
    }

    /// <summary>
    /// What the phone shows about a code before anything is typed: which device asks, and the code's state. It needs
    /// no sign-in and involves no account, so only the row is locked.
    /// </summary>
    internal static Task<AuthOutcome> ReadDeviceAuthorizationAsync(IdentityAdmissionServices services, string? userCode,
        string address, CancellationToken ct) => AtBoundary(async () =>
    {
        var (found, code, refusal) = await FindDeviceAsync(services, userCode, address, ct);
        if (refusal is not null) return refusal;
        return await services.Store.AdmitDeviceAuthorizationAsync<AuthOutcome>(found!.ID, row =>
        {
            if (Recheck(services, row, found.UserCodeDigest!, services.Clock.GetUtcNow(), out var displayName) is { } changed) return changed;
            return DeviceView(row!, code, displayName, null);
        }, ct);
    });

    /// <summary>
    /// Refuses the code a device shows. It needs no sign-in: anyone who can see the code may deny it, and the device
    /// then asks for a new one. No account is involved, so only the row is locked.
    /// </summary>
    internal static Task<AuthOutcome> DenyDeviceAuthorizationAsync(IdentityAdmissionServices services,
        DeviceUserCodeRequest request, string address, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var (found, code, refusal) = await FindDeviceAsync(services, request.UserCode, address, ct);
        if (refusal is not null) return refusal;
        return await services.Store.AdmitDeviceAuthorizationAsync<AuthOutcome>(found!.ID, row =>
        {
            if (Recheck(services, row, found.UserCodeDigest!, services.Clock.GetUtcNow(), out var displayName) is { } changed) return changed;
            if (row!.State == DeviceAuthorizationState.Expired) return Refuse(AuthenticationFailure.Expired);
            if (row.State != DeviceAuthorizationState.Pending) return Refuse(AuthenticationFailure.StaleOperation);
            row.State = DeviceAuthorizationState.Denied;
            return DeviceView(row, code, displayName, null);
        }, ct);
    });

    /// <summary>
    /// Signs the device that shows the code in as the account whose username and password are typed on the phone. The
    /// phone itself never signs in. The password is checked as the login checks it: a wrong one counts against the
    /// account, and an account out of attempts is refused. Only after a correct password is the account asked whether
    /// it allows device sign-in, so the answer never tells a stranger which accounts do. An account that owes a step
    /// (a password change, a recovery, a verified email) must sign in normally first; it is never asked for MFA here.
    /// The lock order is the account, then the row, as for the issue.
    /// </summary>
    internal static Task<AuthOutcome> ApproveDeviceAuthorizationAsync(IdentityAdmissionServices services,
        ApproveDeviceRequest request, string address, CancellationToken ct) => AtBoundary(async () =>
    {
        var startedAt = services.Clock.GetUtcNow();
        if (request is null) return Refuse(AuthenticationFailure.InvalidRequest);
        request = request with { Username = request.Username?.Trim()! };
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var (found, code, refusal) = await FindDeviceAsync(services, request.UserCode, address, ct);
        if (refusal is not null) return refusal;
        var snapshot = await services.Store.ReadProofAsync(request.Username, services.Client, ct);
        if (snapshot is null) return Refuse(AuthenticationFailure.InvalidProof);
        var validPassword = HashService.VerifyVersionedPassword(request.Password, snapshot.User.Salt, snapshot.User.PasswordHash);
        var provenAt = services.Clock.GetUtcNow();
        var upgrade = validPassword && VersionedPasswordHash.NeedsUpgrade(snapshot.User.PasswordHash)
            ? HashService.GenerateVersionedHash(request.Password) : null;
        services.Observe?.Invoke("PasswordProof");
        return await services.Store.AdmitAsync<AuthOutcome>(snapshot.User.ID, null, services.Client, async unit =>
        {
            var now = services.Clock.GetUtcNow();
            var common = CommonRefusal(services, unit, snapshot.Security.SecurityVersion, snapshot.Policy.Revision);
            if (common is not null) return common;
            if (!CryptographicOperations.FixedTimeEquals(snapshot.User.PasswordHash, unit.User.PasswordHash) ||
                !CryptographicOperations.FixedTimeEquals(snapshot.User.Salt, unit.User.Salt) ||
                snapshot.Security.FactorGeneration != unit.Security.FactorGeneration)
                return Refuse(AuthenticationFailure.StaleOperation);
            if (BudgetExhausted(unit.Security, now) || unit.User.LockDownUntil is { } lockedUntil && lockedUntil > now.UtcDateTime)
                return Refuse(AuthenticationFailure.AttemptsExhausted);
            if (!validPassword)
            {
                FailedProof(unit.Security, now);
                unit.Audit("InvalidPassword", now, found!.ID);
                return Refuse(AuthenticationFailure.InvalidProof);
            }
            if (now >= startedAt.AddMinutes(5)) return Refuse(AuthenticationFailure.Expired);
            if (upgrade is not null)
            {
                unit.User.PasswordHash = upgrade.PasswordHash;
                unit.User.Salt = upgrade.Salt;
            }
            if (!unit.User.AllowDeviceSignIn)
            {
                unit.Audit("DeviceSignInRefused", now, found!.ID);
                return Refuse(AuthenticationFailure.DeviceSignInNotAllowed);
            }
            // The device's session must be able to renew, so the account may owe nothing.
            if (LocalStep(unit, false) is { } step) return Restricted(step, now);
            var row = await services.Store.LockDeviceAuthorizationAsync(found!.ID, ct);
            services.Observe?.Invoke("DeviceLocked");
            if (Recheck(services, row, found.UserCodeDigest!, now, out var displayName) is { } changed) return changed;
            if (row!.State == DeviceAuthorizationState.Expired) return Refuse(AuthenticationFailure.Expired);
            if (row.State != DeviceAuthorizationState.Pending) return Refuse(AuthenticationFailure.StaleOperation);
            // The account's state and the password's proof, pinned for the issue.
            row.State = DeviceAuthorizationState.Approved;
            row.UserID = unit.User.ID;
            row.SecurityVersion = unit.Security.SecurityVersion;
            row.PolicyRevision = unit.Policy.Revision;
            row.FactorGeneration = unit.Security.FactorGeneration;
            row.MfaSatisfied = false;
            row.AuthenticatedAt = provenAt;
            row.SessionProvider = null;
            row.ApprovedAt = now;
            unit.Security.FailedProofs = 0;
            unit.Security.FailureWindowStart = null;
            unit.Audit("DeviceApproved", now, row.ID);
            return DeviceView(row, code, displayName, unit.User.FullName);
        }, ct);
    });

    // Every phone request costs the caller's address, and a code that matches nothing costs it again from a smaller
    // budget: user codes are short, so guessing is limited per address.
    private static async Task<(DeviceAuthorization? Row, string Code, AuthenticationRefused? Refusal)> FindDeviceAsync(
        IdentityAdmissionServices services, string? userCode, string address, CancellationToken ct)
    {
        if (services.Device is null) return (null, "", Refuse(AuthenticationFailure.ClientDenied));
        var limits = services.DeliveryLimits;
        if (!await services.Store.ConsumeIngressAsync(DeviceKey(services, "device-lookup:" + address), services.Clock.GetUtcNow(),
            limits.DeviceLookupsPerIpPer15Minutes, DeviceThrottleWindow, ct))
            return (null, "", Refuse(AuthenticationFailure.AttemptsExhausted));
        var failures = DeviceKey(services, "device-lookup-failure:" + address);
        if (await services.Store.IngressExhaustedAsync(failures, services.Clock.GetUtcNow(), limits.DeviceLookupFailuresPerIpPer15Minutes, DeviceThrottleWindow, ct))
            return (null, "", Refuse(AuthenticationFailure.AttemptsExhausted));
        var normalized = DeviceUserCode.Normalize(userCode);
        var found = normalized is null ? null : await services.Store.FindDeviceAuthorizationAsync(UserCodeDigest(services, normalized), ct);
        if (found?.UserCodeDigest is null)
        {
            await services.Store.ConsumeIngressAsync(failures, services.Clock.GetUtcNow(), limits.DeviceLookupFailuresPerIpPer15Minutes, DeviceThrottleWindow, ct);
            return (null, "", Refuse(AuthenticationFailure.InvalidGrant));
        }
        services.Observe?.Invoke("DeviceUserCode");
        return (found, normalized!, null);
    }

    // Under the row's lock: the row still holds this user code and belongs to a device client the host still has. A
    // row whose deadline has passed expires here. Null means the caller may go on with the row.
    private static AuthenticationRefused? Recheck(IdentityAdmissionServices services, DeviceAuthorization? row, byte[] digest,
        DateTimeOffset now, out string displayName)
    {
        displayName = "";
        if (row?.UserCodeDigest is not { } stored || !CryptographicOperations.FixedTimeEquals(stored, digest))
            return Refuse(AuthenticationFailure.InvalidGrant);
        if (services.Device is not { } device || !device.Clients.TryGetValue(row.ClientID, out var name) ||
            row.Audience != services.Client.Audience)
            return Refuse(AuthenticationFailure.ClientDenied);
        displayName = name;
        if (row.State is DeviceAuthorizationState.Pending or DeviceAuthorizationState.Approved && now >= row.ExpiresAt)
            ExpireDevice(row);
        return null;
    }

    private static DeviceAuthorizationView DeviceView(DeviceAuthorization row, string userCode, string displayName, string? accountName) =>
        new(DeviceUserCode.Format(userCode), displayName, accountName, row.State, row.CreatedAt, row.ExpiresAt);

    private static void ExpireDevice(DeviceAuthorization row)
    {
        row.State = DeviceAuthorizationState.Expired;
        row.DeviceCodeDigest = null;
        row.UserCodeDigest = null;
    }

    private static string NewUserCode()
    {
        Span<char> letters = stackalloc char[DeviceUserCode.Length];
        for (var i = 0; i < letters.Length; i++) letters[i] = DeviceUserCode.Alphabet[RandomNumberGenerator.GetInt32(DeviceUserCode.Alphabet.Length)];
        return new string(letters);
    }

    private static byte[] UserCodeDigest(IdentityAdmissionServices services, string normalized) =>
        HMACSHA256.HashData(services.Options.OperationKey, Encoding.ASCII.GetBytes("device-user-code:" + normalized));

    // Throttle keys are digests, never raw addresses or account numbers.
    private static string DeviceKey(IdentityAdmissionServices services, string value) =>
        Convert.ToHexString(HMACSHA256.HashData(services.Options.OperationKey, Encoding.UTF8.GetBytes(value)));

    /// <summary>A refusal on the device token route, with the OAuth error code that tells the screen what to do next.</summary>
    internal static AuthenticationRefused DeviceRefusal(AuthenticationFailure code) => new(code, Error: code switch
    {
        AuthenticationFailure.AuthorizationPending => "authorization_pending",
        AuthenticationFailure.SlowDown or AuthenticationFailure.AttemptsExhausted => "slow_down",
        AuthenticationFailure.ExpiredToken => "expired_token",
        AuthenticationFailure.InvalidRequest => "invalid_request",
        AuthenticationFailure.InvalidGrant => "invalid_grant",
        AuthenticationFailure.Unavailable => "temporarily_unavailable",
        // Denied, or the account can no longer sign the device in (inactive, deleted, changed, no longer allowed, client removed).
        _ => "access_denied"
    });
}
