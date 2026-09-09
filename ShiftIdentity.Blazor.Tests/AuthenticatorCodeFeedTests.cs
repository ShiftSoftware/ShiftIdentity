using ShiftSoftware.ShiftIdentity.Blazor.Services;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

[Trait("Category", "Ui")]
public sealed class AuthenticatorCodeFeedTests
{
    private static readonly DateTimeOffset ServerTime = DateTimeOffset.FromUnixTimeSeconds(1800000000);
    private static AuthenticatorCodeSnapshot Snapshot(double seconds = 30) =>
        new(ServerTime, ServerTime.AddSeconds(seconds), [new("synthetic", "001234")]);

    [Fact]
    public async Task Countdown_uses_server_lifetime_and_elapsed_time_and_renews_only_at_expiry()
    {
        var clock = new Clock(); using var feed = new AuthenticatorCodeFeed(clock); var reads = 0;
        Task<AuthenticatorCodeSnapshot> Load(CancellationToken _) { reads++; return Task.FromResult(Snapshot()); }
        await feed.RefreshIfDueAsync(Load);
        Assert.Equal("001234", feed.Current!.Accounts[0].Code);
        clock.Advance(9.2); await feed.RefreshIfDueAsync(Load);
        Assert.Equal(21, feed.SecondsRemaining); Assert.Equal(1, reads);
        clock.WallTime = clock.WallTime.AddDays(-100);
        clock.Advance(20.8);
        Assert.Null(feed.Current); Assert.Equal(0, feed.SecondsRemaining);
        await feed.RefreshIfDueAsync(Load);
        Assert.Equal(2, reads); Assert.Equal(30, feed.SecondsRemaining);
    }

    [Fact]
    public async Task Whole_round_trip_is_deducted_so_a_slow_response_cannot_extend_validity()
    {
        var clock = new Clock(); using var feed = new AuthenticatorCodeFeed(clock);
        await feed.RefreshIfDueAsync(_ => { clock.Advance(4); return Task.FromResult(Snapshot(10)); });
        Assert.Equal(6, feed.SecondsRemaining);
        clock.Advance(6); Assert.Null(feed.Current);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(31)]
    public async Task Invalid_server_window_is_never_displayed(double seconds)
    {
        using var feed = new AuthenticatorCodeFeed(new Clock());
        await feed.RefreshIfDueAsync(_ => Task.FromResult(Snapshot(seconds)));
        Assert.Null(feed.Current); Assert.NotNull(feed.Error);
    }

    [Fact]
    public async Task Expired_response_and_failure_hide_codes_and_limit_retries()
    {
        var clock = new Clock(); using var feed = new AuthenticatorCodeFeed(clock); var reads = 0;
        Task<AuthenticatorCodeSnapshot> Load(CancellationToken _) { reads++; throw new HttpRequestException(); }
        await feed.RefreshIfDueAsync(_ => { clock.Advance(2); return Task.FromResult(Snapshot(1)); });
        Assert.Null(feed.Current);
        await feed.RefreshIfDueAsync(Load);
        for (var i = 0; i < 20; i++) await feed.RefreshIfDueAsync(Load);
        Assert.Equal(1, reads); Assert.Null(feed.Current);
        clock.Advance(2); await feed.RefreshIfDueAsync(_ => Task.FromResult(Snapshot()));
        Assert.NotNull(feed.Current); Assert.Null(feed.Error);
        clock.Advance(30); await feed.RefreshIfDueAsync(Load);
        Assert.Null(feed.Current); Assert.NotNull(feed.Error);
    }

    [Fact]
    public async Task Visibility_resume_requires_a_fresh_snapshot_and_hidden_tabs_make_no_requests()
    {
        var clock = new Clock(); using var feed = new AuthenticatorCodeFeed(clock); var reads = 0;
        Task<AuthenticatorCodeSnapshot> Load(CancellationToken _) { reads++; return Task.FromResult(Snapshot()); }
        await feed.RefreshIfDueAsync(Load);
        feed.SetVisible(false); clock.Advance(60);
        await feed.RefreshIfDueAsync(Load);
        Assert.Null(feed.Current); Assert.Equal(1, reads);
        feed.SetVisible(true); Assert.Null(feed.Current);
        await feed.RefreshIfDueAsync(Load); Assert.Equal(2, reads); Assert.NotNull(feed.Current);
    }

    [Fact]
    public async Task Context_change_cancels_inflight_read_and_late_result_cannot_restore_old_codes()
    {
        var clock = new Clock(); using var feed = new AuthenticatorCodeFeed(clock);
        var pending = new TaskCompletionSource<AuthenticatorCodeSnapshot>(); CancellationToken token = default;
        var first = feed.RefreshIfDueAsync(ct => { token = ct; return pending.Task; });
        await feed.RefreshIfDueAsync(_ => throw new Exception("Overlapping request"));
        feed.Invalidate(); Assert.True(token.IsCancellationRequested);
        pending.SetResult(Snapshot()); await first;
        Assert.Null(feed.Current);
        clock.Advance(2); await feed.RefreshIfDueAsync(_ => Task.FromResult(Snapshot()));
        Assert.NotNull(feed.Current);
    }

    [Fact]
    public async Task Disposal_cancels_read_and_prevents_any_late_display_or_new_request()
    {
        var feed = new AuthenticatorCodeFeed(new Clock()); CancellationToken token = default;
        var pending = new TaskCompletionSource<AuthenticatorCodeSnapshot>();
        var running = feed.RefreshIfDueAsync(ct => { token = ct; return pending.Task; });
        feed.Dispose(); Assert.True(token.IsCancellationRequested);
        await running; pending.SetResult(Snapshot());
        await feed.RefreshIfDueAsync(_ => throw new Exception("Disposed request"));
        Assert.Null(feed.Current);
    }

    private sealed class Clock : TimeProvider
    {
        private long timestamp;
        public DateTimeOffset WallTime = ServerTime.AddYears(5);
        public override DateTimeOffset GetUtcNow() => WallTime;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public void Advance(double seconds) => timestamp += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
