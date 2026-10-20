using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using ShiftMapper;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Replication;
using Xunit;

namespace ShiftIdentity.Data.Tests;

/// <summary>
/// The Identity replication mapper in a process that has just started, where the first maps can all come at once: the
/// catch-up timers of a Functions host fire together, and one rename of a part starts a sync for each of its branches.
/// ShiftMapper used to fill a mapper's store on its first map without a lock (FE-2026-10-18-02), so concurrent first
/// maps saw a half-filled store and could break it for the life of the process. ShiftMapper now fills it once, under a
/// lock, and this pins that for the identity mapper.
/// </summary>
public class IdentityReplicationMapperConcurrencyTests
{
    [Fact]
    public async Task Concurrent_first_maps_on_a_fresh_provider_all_succeed()
    {
        const int providers = 40;
        const int concurrency = 8;
        var failures = new ConcurrentQueue<string>();

        for (var round = 0; round < providers; round++)
        {
            await using var provider = new ServiceCollection().AddShiftIdentityReplicationMapper().BuildServiceProvider();
            using var start = new ManualResetEventSlim();

            // Each map resolves the mapper in a scope of its own, as each sync and each timer does, and all of them are
            // released together. A thread each, so none waits for the thread pool to grow.
            var maps = Enumerable.Range(0, concurrency).Select(i => Task.Factory.StartNew(() =>
            {
                start.Wait();
                try
                {
                    using var scope = provider.CreateScope();
                    var mapper = scope.ServiceProvider.GetRequiredService<IMapper>();
                    _ = (i % 4) switch
                    {
                        0 => (object)mapper.Map<CompanyBranch, BranchModel>(new CompanyBranch { Name = "Main" }),
                        1 => mapper.Map<City, CityModel>(new City { Name = "Erbil" }),
                        2 => mapper.Map<Team, TeamModel>(new Team { Name = "Ops" }),
                        _ => mapper.Map<Region, CityRegionModel>(new Region { Name = "KRG" }),
                    };
                }
                catch (Exception ex)
                {
                    failures.Enqueue($"{ex.GetType().Name}: {ex.Message}");
                }
            }, TaskCreationOptions.LongRunning)).ToArray();

            start.Set();
            await Task.WhenAll(maps);
        }

        Assert.Empty(failures);
    }
}
