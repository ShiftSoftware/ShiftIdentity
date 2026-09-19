using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Authentication;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>Maintenance for a host that enabled the authority: expired operations are cleaned up once a minute.</summary>
internal sealed class AdmissionMaintenance(IServiceScopeFactory scopes, ILogger<AdmissionMaintenance> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var admission = scope.ServiceProvider.GetRequiredService<IdentityAdmissionServices>();
                    var db = scope.ServiceProvider.GetRequiredService<ShiftIdentityDbContext>();
                    await new SqlIdentitySecurityStore(db).CleanupAsync(admission.Clock.GetUtcNow(), stoppingToken);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    // No exception payload: provider diagnostics can include sensitive SQL parameters.
                    logger.LogWarning("Identity operation cleanup was unavailable; the next interval will retry. Expired operations remain unusable.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
