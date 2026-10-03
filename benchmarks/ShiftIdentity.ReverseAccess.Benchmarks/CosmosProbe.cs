using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Data.IdentityReference;
using ShiftSoftware.ShiftIdentity.Data.IdentityReference.Cosmos;

namespace ShiftIdentity.ReverseAccess.Benchmarks;

internal static class CosmosProbe
{
    public static async Task<object> Run(Settings settings)
    {
        var connection = settings.Value("CosmosDb:ConnectionString") ?? throw new InvalidOperationException();
        var endpoint = new Uri(connection.Split(';').First(x => x.StartsWith("AccountEndpoint=", StringComparison.OrdinalIgnoreCase)).Split('=',2)[1]);
        if (!endpoint.IsLoopback) throw new InvalidOperationException("The benchmark probe only permits local Cosmos.");
        using var client = new CosmosClient(connection, new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            RequestTimeout = TimeSpan.FromSeconds(15), MaxRetryAttemptsOnRateLimitedRequests = 0,
            HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                // Emulator-only. The explicit loopback check above prevents this from reaching a remote service.
                ServerCertificateCustomValidationCallback = (request, _, _, _) => request.RequestUri!.IsLoopback,
            }),
        });
        var options = Options.Create(new CosmosIdentityReferenceOptions
            { DatabaseName = settings.Value("CosmosDb:DatabaseName") ?? "Identity" });
        var output = new List<object>();
        for (var iteration = 0; iteration < 5; iteration++)
        {
            var source = new InstrumentedSource(client, options);
            var sw = Stopwatch.StartNew();
            var users = await source.GetUsersAsync();
            var companies = await source.GetCompaniesAsync();
            var branches = await source.GetCompanyBranchesAsync();
            output.Add(new { Operation = "reference-source-cold", Iteration = iteration, Milliseconds = sw.Elapsed.TotalMilliseconds,
                Users = users.Count, Companies = companies.Count, Branches = branches.Count,
                UsersWithAuthorizationState = users.Values.Count(u => u.IsActive.HasValue),
                Pages = source.Pages.Count, RequestUnits = source.Pages.Sum(p => p.Ru),
                SdkElapsedMilliseconds = source.Pages.Sum(p => p.Ms) });
        }
        var concurrent = new InstrumentedSource(client, options);
        var timer = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0,8).Select(_ => concurrent.GetUsersAsync()));
        output.Add(new { Operation = "concurrent-8-first-user-read", Milliseconds = timer.Elapsed.TotalMilliseconds,
            Pages = concurrent.Pages.Count, RequestUnits = concurrent.Pages.Sum(p => p.Ru) });
        var before = concurrent.Pages.Count;
        timer.Restart();
        for (var i=0;i<1000;i++) await concurrent.GetUsersAsync();
        output.Add(new { Operation = "warm-user-read-1000", Milliseconds = timer.Elapsed.TotalMilliseconds, Pages = concurrent.Pages.Count-before });
        before = concurrent.Pages.Count;
        var ruBefore = concurrent.Pages.Sum(p => p.Ru);
        timer.Restart();
        await concurrent.RefreshAsync(IdentityReferenceFamily.Users);
        output.Add(new { Operation = "refresh-users", Milliseconds = timer.Elapsed.TotalMilliseconds,
            Pages = concurrent.Pages.Count-before, RequestUnits = concurrent.Pages.Sum(p => p.Ru)-ruBefore });
        foreach (var family in new[] { "AccessTrees", "UserAccessTrees", "TeamUsers" })
        {
            try
            {
                var source = new InstrumentedSource(client, options);
                timer.Restart();
                var count = family switch
                {
                    "AccessTrees" => (await source.GetAccessTreesAsync()).Count,
                    "UserAccessTrees" => (await source.GetUserAccessTreesAsync()).Count,
                    _ => (await source.GetTeamUsersAsync()).Count,
                };
                output.Add(new { Operation = family, Count = count, Milliseconds = timer.Elapsed.TotalMilliseconds,
                    Pages = source.Pages.Count, RequestUnits = source.Pages.Sum(p => p.Ru) });
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            { output.Add(new { Operation = family, Status = "not-found" }); }
        }
        return new { Source = "local-emulator", Mode = "Gateway", ProductionRepresentative = false, Results = output };
    }

    private sealed class InstrumentedSource(CosmosClient client, IOptions<CosmosIdentityReferenceOptions> options)
        : CosmosIdentityReferenceSource<CosmosClient>(client, options)
    {
        public ConcurrentBag<(double Ru,double Ms)> Pages { get; } = [];
        internal override async Task<IReadOnlyList<T>> ReadRowsAsync<T>(string containerName, string? itemType, string? id, CancellationToken ct)
        {
            var query = new QueryDefinition(itemType is null ? "SELECT * FROM c" : "SELECT * FROM c WHERE c.ItemType = @itemType");
            if (itemType is not null) query.WithParameter("@itemType", itemType);
            // This probe only exercises whole-family reads/refresh, matching the baseline's queries.
            if (id is not null) throw new NotSupportedException();
            using var iterator = client.GetContainer(options.Value.DatabaseName, containerName).GetItemQueryIterator<T>(query);
            var rows = new List<T>();
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(ct);
                Pages.Add((page.RequestCharge,page.Diagnostics.GetClientElapsedTime().TotalMilliseconds));
                rows.AddRange(page);
            }
            return rows;
        }
    }
}
