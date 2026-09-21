using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Enums;

namespace ShiftSoftware.ShiftIdentity.Blazor;

/// <summary>Owns token renewal for the host. Customize persistence through <see cref="IIdentityTokenStorage"/>.</summary>
public sealed class IdentitySession
{
    private readonly IIdentityTokenStorage storage;
    private readonly IdentityRenewalTransport transport;
    private readonly SemaphoreSlim state = new(1, 1);
    private long generation;
    private Renewal? renewal;
    internal event Action? Changed;
    internal bool NotifyOnAuthStateRead => !transport.NotifyChanges;

    internal IdentitySession(IIdentityTokenStorage storage, IdentityRenewalTransport transport)
    {
        this.storage = storage;
        this.transport = transport;
    }

    /// <summary>Reads the current access token synchronously for authentication state. Use GetTokenAsync for requests.</summary>
    public string? GetToken() => transport.Readable(storage.Read())?.Token;

    /// <summary>Returns the stored token, renewing ordinary sessions when missing or within ten seconds of expiry.</summary>
    public async Task<TokenDTO?> GetTokenAsync() => (await ReadOrRenewAsync(false)).Token;

    public async Task StoreTokenAsync(TokenDTO token)
    {
        transport.ValidateForStorage(token);
        await state.WaitAsync();
        try
        {
            generation++;
            await storage.WriteAsync(token);
        }
        finally { state.Release(); }
        Notify();
    }

    internal sealed record Checkpoint(long Generation, string? Access, string? Refresh);

    internal async Task<Checkpoint> ReadCheckpointAsync()
    {
        await state.WaitAsync();
        try
        {
            var token = await storage.ReadAsync();
            return new(generation, token?.Token, token?.RefreshToken);
        }
        finally { state.Release(); }
    }

    internal async Task<bool> MatchesAsync(Checkpoint expected) => expected == await ReadCheckpointAsync();

    internal async Task<bool> TryStoreTokenAsync(Checkpoint expected, TokenDTO replacement)
    {
        transport.ValidateForStorage(replacement);
        await state.WaitAsync();
        try
        {
            var current = await storage.ReadAsync();
            if (expected.Generation != generation || expected.Access != current?.Token || expected.Refresh != current?.RefreshToken) return false;
            generation++;
            await storage.WriteAsync(replacement);
        }
        finally { state.Release(); }
        Notify();
        return true;
    }

    public async Task RemoveTokenAsync()
    {
        await state.WaitAsync();
        try
        {
            generation++;
            await storage.RemoveAsync();
        }
        finally { state.Release(); }
        Notify();
    }

    /// <summary>Renews through the same single-flight and storage-generation checks as refresh on read.</summary>
    internal async Task<bool> RenewAsync() => (await ReadOrRenewAsync(true)).Renewed;

    private async Task<SessionRead> ReadOrRenewAsync(bool force)
    {
        Renewal pending;
        var start = false;
        await state.WaitAsync();
        try
        {
            var before = await storage.ReadAsync();
            if (before is null) return new(null, false);
            // Restricted legacy journeys cannot renew and must keep their temporary credential.
            if (before.Flow != AuthPurpose.None || string.IsNullOrWhiteSpace(before.RefreshToken))
                return new(before, false);
            if (!force && !string.IsNullOrWhiteSpace(before.Token) && !JwtUtils.IsExpired(before.Token, 10) &&
                transport.Readable(before) is not null)
                return new(before, false);

            if (renewal is { } current && current.Matches(generation, before)) pending = current;
            else
            {
                // Publish the task before starting HTTP, including when the transport completes synchronously.
                pending = new(generation, before.Token, before.RefreshToken);
                renewal = pending;
                start = true;
            }
        }
        finally { state.Release(); }

        if (start) _ = CompleteRenewalAsync(pending);
        return await pending.Completion.Task;
    }

    private async Task CompleteRenewalAsync(Renewal pending)
    {
        try
        {
            var result = await transport.RenewAsync(pending.RefreshToken);
            SessionRead read;
            var changed = false;
            await state.WaitAsync();
            try
            {
                var current = await storage.ReadAsync();
                // Snapshot both strings: another tab can change storage without using this session.
                // Keep the gate through persistence so logout or a new login also wins during a slow write.
                if (!pending.Matches(generation, current))
                    read = new(transport.Readable(current), transport.Readable(current) is not null);
                else if (result.Token is { } token)
                {
                    generation++;
                    await storage.WriteAsync(token);
                    changed = true;
                    read = new(token, true);
                }
                else if (result.Remove)
                {
                    generation++;
                    await storage.RemoveAsync();
                    changed = true;
                    read = new(null, false);
                }
                else read = new(result.KeepCurrentAccess ? transport.Readable(current) : null, false);

                if (ReferenceEquals(renewal, pending)) renewal = null;
            }
            finally { state.Release(); }
            if (changed) Notify();
            pending.Completion.TrySetResult(read);
        }
        catch (Exception error)
        {
            await state.WaitAsync();
            try { if (ReferenceEquals(renewal, pending)) renewal = null; }
            finally { state.Release(); }
            pending.Completion.TrySetException(error);
        }
    }

    // Legacy message-handler cleanup deliberately does not redirect an in-progress page.
    private void Notify() { if (transport.NotifyChanges) Changed?.Invoke(); }

    private sealed record SessionRead(TokenDTO? Token, bool Renewed);
    private sealed record Renewal(long Generation, string? AccessToken, string RefreshToken)
    {
        public TaskCompletionSource<SessionRead> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Matches(long generation, TokenDTO? token) => Generation == generation &&
            token is not null && token.Token == AccessToken && token.RefreshToken == RefreshToken;
    }
}
