using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Services;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Policy")]
public sealed class HostSecurityEmailSinkTests
{
    private static ShiftIdentityConfiguration Settings => new() { FrontEndUrl = "https://dashboard.example.invalid/app/" };
    private static SecurityEmail Message(AuthenticationOperationPurpose purpose) => new(Guid.NewGuid(), "saved@example.invalid",
        "Security message", "opaque/secret+value", purpose, DateTimeOffset.UtcNow.AddMinutes(30))
        { UserID = "encoded-user", Username = "saved-user", FullName = "Saved Name" };

    [Theory]
    [InlineData(AuthenticationOperationPurpose.EmailVerify, "VerifyEmail")]
    [InlineData(AuthenticationOperationPurpose.PasswordResetEmail, "ResetPassword")]
    public async Task Sends_only_the_bound_snapshot_and_a_dashboard_fragment_link(AuthenticationOperationPurpose purpose, string page)
    {
        var provider = new Provider();
        var sink = new HostSecurityEmailSink(Settings, [provider], [provider]);
        await sink.DeliverAsync(Message(purpose), TestContext.Current.CancellationToken);
        var delivery = Assert.Single(provider.Deliveries);
        Assert.Equal(purpose == AuthenticationOperationPurpose.EmailVerify, delivery.Verification);
        var uri = new Uri(delivery.Link);
        Assert.Equal("/app/Identity/" + page, uri.AbsolutePath);
        Assert.Equal("", uri.Query);
        Assert.Equal("#grant=opaque%2Fsecret%2Bvalue&purpose=" + purpose, uri.Fragment);
        Assert.Equal(("encoded-user", "saved@example.invalid", "saved-user", "Saved Name"),
            (delivery.User.ID, delivery.User.Email, delivery.User.Username, delivery.User.FullName));
    }

    [Fact]
    public async Task Acceptance_waits_for_every_provider_and_propagates_failure_without_retry()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new Provider { Pending = release.Task };
        var second = new Provider { Error = true };
        var sink = new HostSecurityEmailSink(Settings, [first, second], [first]);
        var task = sink.DeliverAsync(Message(AuthenticationOperationPurpose.EmailVerify), TestContext.Current.CancellationToken);
        Assert.False(task.IsCompleted); Assert.Empty(second.Deliveries);
        release.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Single(first.Deliveries); Assert.Single(second.Deliveries);
    }

    [Fact]
    public async Task Cancellation_stops_the_wait_and_does_not_call_the_next_provider()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new Provider { Pending = release.Task }; var second = new Provider();
        using var stop = new CancellationTokenSource();
        var sink = new HostSecurityEmailSink(Settings, [first, second], [first]);
        var task = sink.DeliverAsync(Message(AuthenticationOperationPurpose.EmailVerify), stop.Token);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        release.SetResult(); // The old interface cannot cancel external I/O; late results cannot resume the adapter.
        Assert.Single(first.Deliveries); Assert.Empty(second.Deliveries);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/dashboard")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:password@example.invalid")]
    [InlineData("https://dashboard.example.invalid/?return=evil")]
    [InlineData("https://dashboard.example.invalid/#fragment")]
    public void Startup_refuses_invalid_dashboard_addresses(string? address)
    {
        var provider = new Provider(); var settings = Settings; settings.FrontEndUrl = address;
        Assert.Contains("FrontEndUrl", Assert.Throws<InvalidOperationException>(() =>
            new HostSecurityEmailSink(settings, [provider], [provider]).CheckReady()).Message);
    }

    [Fact]
    public async Task Staged_provider_gets_shared_content_and_replaces_only_the_legacy_fallback()
    {
        var old = new Provider(); var sender = new StagedProvider();
        var message = Message(AuthenticationOperationPurpose.EmailVerify);
        var sink = new HostSecurityEmailSink(Settings, [old], [old], [sender]);
        sink.CheckReady();
        await sink.DeliverAsync(message, TestContext.Current.CancellationToken);
        var content = Assert.Single(sender.Deliveries);
        Assert.Equal(message.ID, content.ID); Assert.Equal(message.Destination, content.Destination);
        Assert.Contains("Saved Name", content.HtmlBody); Assert.Contains("saved-user", content.TextBody);
        Assert.Contains(message.ExpiresAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"), content.TextBody);
        Assert.Empty(old.Deliveries);
        new HostSecurityEmailSink(Settings, [], [], [sender]).CheckReady();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Staged_acceptance_is_awaited_and_cancellation_reaches_IO_before_later_providers(bool cancel)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new StagedProvider { Pending = release.Task }; var next = new StagedProvider();
        using var stop = new CancellationTokenSource();
        var sink = new HostSecurityEmailSink(Settings, [], [], [first, next]);
        var send = sink.DeliverAsync(Message(AuthenticationOperationPurpose.PasswordResetEmail), stop.Token);
        Assert.False(send.IsCompleted); Assert.Empty(next.Deliveries);
        if (cancel)
        {
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
            Assert.True(first.Token.IsCancellationRequested); Assert.Empty(next.Deliveries);
        }
        else
        {
            release.SetException(new InvalidOperationException("Synthetic failure"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => send);
            Assert.Empty(next.Deliveries);
        }
    }

    private sealed class StagedProvider : ISecurityEmailSender
    {
        public List<SecurityEmailContent> Deliveries { get; } = [];
        public Task Pending { get; init; } = Task.CompletedTask;
        public CancellationToken Token { get; private set; }
        public async Task SendAsync(SecurityEmailContent message, CancellationToken cancellationToken)
        {
            Token = cancellationToken; Deliveries.Add(message);
            await Pending.WaitAsync(cancellationToken);
        }
    }

    [Theory]
    [InlineData(true, false, "ISendEmailResetPassword")]
    [InlineData(false, true, "ISendEmailVerification")]
    public void Startup_names_the_missing_provider(bool verify, bool reset, string missing)
    {
        var provider = new Provider();
        var sink = new HostSecurityEmailSink(Settings, verify ? [provider] : [], reset ? [provider] : []);
        Assert.Contains(missing, Assert.Throws<InvalidOperationException>(sink.CheckReady).Message);
    }

    [Theory]
    [InlineData(typeof(IUserAccountAuthority))]
    [InlineData(typeof(IIdentitySecurityStore))]
    [InlineData(typeof(ISecurityEmailSink))]
    public void Startup_names_a_missing_adapter_before_resolving_dependencies(Type missing)
    {
        var services = new ServiceCollection();
        services.AddScoped<IUserAccountAuthority, AdmissionUserAccountAuthority>();
        services.AddScoped<IIdentitySecurityStore, SqlIdentitySecurityStore>();
        services.AddScoped<IdentityAdmissionServices>(_ => throw new InvalidOperationException("Must not resolve before completeness check"));
        services.AddSingleton<ISecurityEmailSink>(new LocalSecurityInbox());
        services.RemoveAll(missing);
        using var container = services.BuildServiceProvider();
        Assert.Contains(missing.Name, Assert.Throws<InvalidOperationException>(() => IdentityAuthorityStartup.CheckAdapters(container)).Message);
    }

    private sealed class Provider : ISendEmailVerification, ISendEmailResetPassword
    {
        public List<(bool Verification, string Link, UserDataDTO User)> Deliveries { get; } = [];
        public Task Pending { get; init; } = Task.CompletedTask;
        public bool Error { get; init; }
        public Task SendEmailVerificationAsync(string url, UserDataDTO user) => Send(true, url, user);
        public Task SendEmailResetPasswordAsync(string url, UserDataDTO user) => Send(false, url, user);
        private async Task Send(bool verification, string link, UserDataDTO user)
        {
            Deliveries.Add((verification, link, user));
            await Pending;
            if (Error) throw new InvalidOperationException("Synthetic provider failure");
        }
    }
}
