namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public sealed record AuthenticatorCode(string Username, string? Code, long UserID = 0);
public sealed record AuthenticatorCodeSnapshot(DateTimeOffset GeneratedAt, DateTimeOffset ExpiresAt,
    AuthenticatorCode[] Accounts, string? Password = null);

/// <summary>Development code snapshots expire against elapsed time, independent of the browser's wall clock.</summary>
public sealed class AuthenticatorCodeFeed(TimeProvider? clock = null) : IDisposable
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private AuthenticatorCodeSnapshot? snapshot;
    private long requestedAt;
    private long? lastAttempt;
    private int version;
    private bool disposed;
    private CancellationTokenSource? request;
    public bool Visible { get; private set; } = true;
    public bool Busy => request is not null;
    public string? Error { get; private set; }
    public TimeSpan Remaining => snapshot is null ? TimeSpan.Zero :
        TimeSpan.FromMilliseconds(Math.Max(0, (snapshot.ExpiresAt - snapshot.GeneratedAt - clock.GetElapsedTime(requestedAt)).TotalMilliseconds));
    public AuthenticatorCodeSnapshot? Current => Visible && Remaining > TimeSpan.Zero ? snapshot : null;
    public int SecondsRemaining => (int)Math.Ceiling(Remaining.TotalSeconds);

    public void Invalidate()
    {
        version++;
        snapshot = null;
        request?.Cancel();
    }

    public void SetVisible(bool visible)
    {
        Visible = visible;
        Invalidate();
    }

    public async Task RefreshIfDueAsync(Func<CancellationToken, Task<AuthenticatorCodeSnapshot>> load)
    {
        if (disposed || !Visible || Busy || Current is not null ||
            lastAttempt is { } last && clock.GetElapsedTime(last) < TimeSpan.FromSeconds(2)) return;
        snapshot = null;
        Error = null;
        var started = clock.GetTimestamp();
        lastAttempt = started;
        var currentVersion = version;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        request = cancellation;
        try
        {
            var result = await load(cancellation.Token).WaitAsync(cancellation.Token);
            if (disposed || currentVersion != version) return;
            var lifetime = result.ExpiresAt - result.GeneratedAt;
            // Deduct the whole round trip, conservatively including outbound latency and database work.
            if (lifetime <= clock.GetElapsedTime(started) || lifetime > TimeSpan.FromSeconds(30))
                throw new InvalidOperationException("The code snapshot has expired.");
            snapshot = result;
            requestedAt = started;
        }
        catch (Exception)
        {
            if (!disposed && currentVersion == version) Error = "Codes unavailable. Retrying automatically.";
        }
        finally { request = null; }
    }

    public void Dispose()
    {
        disposed = true;
        Invalidate();
    }
}
