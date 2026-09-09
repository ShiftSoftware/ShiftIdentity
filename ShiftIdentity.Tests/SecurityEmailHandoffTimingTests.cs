using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

public sealed partial class SecurityLinkSqlTests
{
    [Theory]
    [InlineData("accepted")]
    [InlineData("unknown")]
    [InlineData("ineligible")]
    [InlineData("throw")]
    [InlineData("timeout")]
    [InlineData("resultFailure")]
    [InlineData("timeoutAndBlockedResult")]
    public async Task Actual_default_public_handoff_latency_covers_all_outcomes(string scenario)
    {
        fixture.DeliveryLimits = new();
        Assert.Equal(3000, fixture.DeliveryLimits.HandoffTimeoutMilliseconds);
        Assert.Equal(2000, fixture.DeliveryLimits.ResultPersistenceTimeoutMilliseconds);
        Assert.Equal(300, fixture.DeliveryLimits.PublicPaddingMilliseconds);
        if (scenario == "ineligible") await Contact(Email, false);
        var sink = new MeasuredHandoffSink(Inbox, scenario);
        fixture.EmailSink = sink;
        var resultFault = scenario is "resultFailure" or "timeoutAndBlockedResult"
            ? new MeasuredHandoffResultFault(new(blockUntilCancellation: scenario == "timeoutAndBlockedResult")) : null;
        using var host = new IdentityHttpHost(fixture, null, null, resultFault is null ? [] : [resultFault]);

        var started = Stopwatch.GetTimestamp();
        using var response = await host.Client.PostAsJsonAsync("/api/identity/v2/password-reset/request",
            new RequestSecurityEmail(scenario == "unknown" ? "absent-default-latency-account" : Email),
            TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var elapsed = Stopwatch.GetElapsedTime(started);
        TestContext.Current.TestOutputHelper!.WriteLine(FormattableString.Invariant(
            $"scenario={scenario} elapsed_ms={elapsed.TotalMilliseconds:F1} expected_floor_ms=5300 sender_calls={sink.Calls} sender_ms={sink.Duration.TotalMilliseconds:F1} sender_cancelled={sink.CancellationObserved} result_fault_calls={resultFault?.Calls ?? 0} result_fault_ms={resultFault?.Duration.TotalMilliseconds ?? 0:F1} result_cancelled={resultFault?.CancellationObserved ?? false}"));

        // The upper bound allows scheduling/SQL variation; this is local default-budget evidence,
        // not a claim of constant latency under arbitrary deployment load.
        Assert.InRange(elapsed.TotalMilliseconds, 5280, 10000);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("{\"kind\":\"deliveryRequested\"}", body);
        Assert.Equal(scenario is "unknown" or "ineligible" ? 0 : 1, sink.Calls);
        Assert.Equal(scenario is "timeout" or "timeoutAndBlockedResult", sink.CancellationObserved);
        if (resultFault is not null)
        {
            Assert.Equal(1, resultFault.Calls);
            Assert.Equal(scenario == "timeoutAndBlockedResult", resultFault.CancellationObserved);
        }
        await using var db = fixture.CreateContext();
        var operations = await db.Set<AuthenticationOperation>().ToArrayAsync(TestContext.Current.CancellationToken);
        if (scenario is "unknown" or "ineligible") Assert.Empty(operations);
        else
        {
            var op = Assert.Single(operations);
            Assert.Equal(scenario is "throw" or "timeout" ? AuthenticationOperationState.Cancelled :
                AuthenticationOperationState.AwaitingExplicitSubmit, op.State);
        }
        await AssertPassword(fixture.Password, 1, false);
    }

    private sealed class MeasuredHandoffSink(ISecurityEmailSink inner, string scenario) : ISecurityEmailSink
    {
        public int Calls { get; private set; }
        public TimeSpan Duration { get; private set; }
        public bool CancellationObserved { get; private set; }
        public async Task DeliverAsync(SecurityEmail message, CancellationToken cancellationToken)
        {
            Calls++;
            var started = Stopwatch.GetTimestamp();
            try
            {
                if (scenario == "throw") throw new IOException("Synthetic immediate handoff failure.");
                if (scenario is "timeout" or "timeoutAndBlockedResult")
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                else await inner.DeliverAsync(message, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { CancellationObserved = true; throw; }
            finally { Duration = Stopwatch.GetElapsedTime(started); }
        }
    }

    private sealed class MeasuredHandoffResultFault(HandoffResultPersistenceFault inner) : SaveChangesInterceptor
    {
        public int Calls => inner.Calls;
        public bool CancellationObserved => inner.CancellationObserved;
        public TimeSpan Duration { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var calls = inner.Calls;
            var started = Stopwatch.GetTimestamp();
            try { return await inner.SavingChangesAsync(eventData, result, cancellationToken); }
            finally { if (inner.Calls != calls) Duration = Stopwatch.GetElapsedTime(started); }
        }
    }
}
