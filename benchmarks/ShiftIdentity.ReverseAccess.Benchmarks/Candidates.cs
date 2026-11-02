using System.Diagnostics;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Core.DataLevelAccess;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Data.Authorization;
using ShiftSoftware.ShiftIdentity.Data.IdentityReference;
using ShiftSoftware.TypeAuth.Core;
using Claims = ShiftSoftware.ShiftEntity.Core.Constants;
using Row = ShiftIdentity.ReverseAccess.Benchmarks.Measurements.Row;

namespace ShiftIdentity.ReverseAccess.Benchmarks;

/// <summary>
/// What one notification event costs. An event resolves its candidates in a new scope (a new resolver over a new
/// source), then checks one record for each eligible subject: the screen permission, then the v2 row check with a new
/// data-level context, as a repository's CanAccessAs does. The documents are the dataset's, replayed in memory, so the
/// times are CPU only. Store reads are counted, not timed.
/// </summary>
internal static class Candidates
{
    private const int BatchEvents = 20;
    private const int Rounds = 5;
    private static int sink;

    public static async Task<object> Run(Dataset data, IHashIdService hashIds, Type[] actions, Dictionary<string, string> arguments)
    {
        var sizes = arguments.GetValueOrDefault("--candidates", "10,100,500").Split(',').Select(int.Parse).ToArray();
        var events = int.Parse(arguments.GetValueOrDefault("--events", "100"));
        var random = new Random(int.Parse(arguments.GetValueOrDefault("--seed", "1")));
        var registry = Measurements.Registry(actions);
        var active = data.Users.Where(u => u.IsActive == true && !u.IsDeleted).Select(u => long.Parse(u.id)).ToArray();
        var records = Measurements.Records(data);
        var simple = Measurements.Policy(false);
        var complex = Measurements.Policy(true);

        // Every subject passes the screen permission, so every check runs the row check: the most an event can cost.
        static bool Screen(IdentityAccessSubject subject, Access access) => true;

        // The whole population once: every user's outcome. It also warms up every path measured below.
        var population = await new IdentitySubjectResolver(new MemorySource(data), registry, hashIds).ResolveAsync(data.Users.Select(u => long.Parse(u.id)));
        var outcomes = new
        {
            Users = data.Users.Count, Eligible = population.Eligible.Count,
            NotEligible = population.NotEligible.GroupBy(x => x.Value).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            Unresolved = population.Unresolved.GroupBy(x => x.Value.Reason).ToDictionary(g => g.Key.ToString(), g => g.Count()),
        };

        var shared = new IdentityReferenceCache(TimeSpan.FromMinutes(5));
        var results = new List<object>();

        foreach (var size in sizes.Where(size => size <= active.Length))
        {
            Console.WriteLine($"Measuring events of {size} candidates.");
            var draws = Enumerable.Range(0, events).Select(_ => Sample(active, size, random)).ToArray();

            // No TTL, the default: each scope reads its documents again. Here a read returns a JSON copy of the rows.
            Collect();
            var cold = new List<double>(); var reads = new List<double>(); var documents = new List<double>();
            foreach (var draw in draws)
            {
                var source = new MemorySource(data, cloneRows: true);
                var started = Stopwatch.GetTimestamp();
                sink = (await new IdentitySubjectResolver(source, registry, hashIds).ResolveAsync(draw)).Eligible.Count;
                cold.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                reads.Add(source.Reads); documents.Add(source.RowsRead);
            }

            // TTL set: every scope reads one shared cache, which is warm.
            foreach (var draw in draws)
                sink = (await new IdentitySubjectResolver(new MemorySource(data, shared), registry, hashIds).ResolveAsync(draw)).Eligible.Count;

            Collect();
            var warm = new List<double>(); var eligible = new List<double>(); var twoDimensions = new List<double>(); var sevenDimensions = new List<double>();
            for (var i = 0; i < draws.Length; i++)
            {
                var started = Stopwatch.GetTimestamp();
                var resolution = await new IdentitySubjectResolver(new MemorySource(data, shared), registry, hashIds).ResolveAsync(draws[i]);
                warm.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                eligible.Add(resolution.Eligible.Count);

                started = Stopwatch.GetTimestamp();
                sink = Measurements.Evaluate(resolution.Eligible, records[i % records.Length], Access.Read, simple, Screen, hashIds).Length;
                twoDimensions.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);

                started = Stopwatch.GetTimestamp();
                sink = Measurements.Evaluate(resolution.Eligible, records[i % records.Length], Access.Read, complex, Screen, hashIds).Length;
                sevenDimensions.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }

            results.Add(new
            {
                Candidates = size, Events = events, EligiblePerEvent = Measurements.Distribution(eligible),
                PrepareNoTtlMs = Measurements.Distribution(cold),
                StoreReadsPerEventNoTtl = Measurements.Distribution(reads), DocumentsReadPerEventNoTtl = Measurements.Distribution(documents),
                PrepareTtlWarmMs = Measurements.Distribution(warm),
                CheckTwoDimensionsMs = Measurements.Distribution(twoDimensions), CheckSevenDimensionsMs = Measurements.Distribution(sevenDimensions),
                ReuseInOneScope = await Reuse(data, registry, hashIds, active, size, shared, random),
            });
        }

        return new
        {
            Method = "Release; the real resolver and source cache over the dataset's documents replayed in memory, so CPU only. " +
                "Each event is a new scope. No TTL: each scope reads the documents again, as JSON copies. TTL: one shared, warm cache. " +
                "The check is the v2 row check of one record for every eligible subject, with a new data-level context per subject; " +
                "every subject passes the screen permission. Candidates are drawn from active users. Percentiles use nearest rank.",
            Outcomes = outcomes,
            Events = results,
            Match = MatchCost(population.Eligible, records, hashIds),
        };
    }

    // A batch of events whose candidates come from one audience of twice the event size: one scope for the whole batch
    // against a new scope per event. The median of a few rounds.
    private static async Task<object> Reuse(Dataset data, ITypeAuthService registry, IHashIdService hashIds, long[] active, int size,
        IdentityReferenceCache shared, Random random)
    {
        var audience = Sample(active, Math.Min(active.Length, size * 2), random);
        var batch = Enumerable.Range(0, BatchEvents).Select(_ => Sample(audience, size, random)).ToArray();

        async Task<double> Time(Func<Task> work)
        {
            var times = new List<double>();
            for (var round = 0; round < Rounds; round++)
            {
                Collect();
                var started = Stopwatch.GetTimestamp();
                await work();
                times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            return times.Order().ElementAt(Rounds / 2);
        }

        async Task Separate(Func<MemorySource> source)
        {
            foreach (var draw in batch)
                sink = (await new IdentitySubjectResolver(source(), registry, hashIds).ResolveAsync(draw)).Eligible.Count;
        }

        async Task Together(Func<MemorySource> source)
        {
            var resolver = new IdentitySubjectResolver(source(), registry, hashIds);
            foreach (var draw in batch)
                sink = (await resolver.ResolveAsync(draw)).Eligible.Count;
        }

        var separateTtl = await Time(() => Separate(() => new MemorySource(data, shared)));
        var togetherTtl = await Time(() => Together(() => new MemorySource(data, shared)));
        var separateNoTtl = await Time(() => Separate(() => new MemorySource(data, cloneRows: true)));
        var togetherNoTtl = await Time(() => Together(() => new MemorySource(data, cloneRows: true)));

        return new
        {
            Events = BatchEvents, Audience = audience.Length,
            PreparedWithAScopePerEvent = batch.Sum(draw => draw.Length), PreparedInOneScope = batch.SelectMany(draw => draw).Distinct().Count(),
            ScopePerEventTtlMs = separateTtl, OneScopeTtlMs = togetherTtl, SavedTtlPercent = Saved(separateTtl, togetherTtl),
            ScopePerEventNoTtlMs = separateNoTtl, OneScopeNoTtlMs = togetherNoTtl, SavedNoTtlPercent = Saved(separateNoTtl, togetherNoTtl),
        };
    }

    // The row check of one dimension declared three ways: a key, a Match compiled once (since S3), and a Match whose
    // factory computes outside the expression, which is compiled on every check, as every Match was before S3.
    private static object MatchCost(IReadOnlyList<IdentityAccessSubject> subjects, Row[] records, IHashIdService hashIds)
    {
        static bool Screen(IdentityAccessSubject subject, Access access) => true;

        static DataLevelAccessPolicy<Row> Branches(Func<DataLevelDimensionBuilder<Row>, DataLevelDimensionBuilder<Row>> predicate)
        {
            var builder = new DataLevelAccessBuilder<Row>();
            predicate(builder.On(ShiftIdentityActions.DataLevelAccess.Branches)).HashId<CompanyBranchDTO>().Self(Claims.CompanyBranchIdClaim);
            return new(builder);
        }

        var policies = new Dictionary<string, DataLevelAccessPolicy<Row>>
        {
            ["Key"] = Branches(dimension => dimension.Key(row => row.Branch)),
            ["MatchCompiledOnce"] = Branches(dimension => dimension.Match(ids => row => ids.Contains(row.Branch))),
            ["MatchCompiledPerCheck"] = Branches(dimension => dimension.Match(ids =>
            {
                var set = ids.ToHashSet();
                return row => set.Contains(row.Branch);
            })),
        };

        var perCheck = new Dictionary<string, object>();
        foreach (var (name, policy) in policies)
        {
            sink = Measurements.Evaluate(subjects, records[0], Access.Read, policy, Screen, hashIds).Length;
            Collect();
            var times = new List<double>();
            for (var round = 0; round < 20; round++)
            {
                var started = Stopwatch.GetTimestamp();
                sink = Measurements.Evaluate(subjects, records[round % records.Length], Access.Read, policy, Screen, hashIds).Length;
                times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds * 1000 / subjects.Count);
            }
            perCheck[name] = Measurements.Distribution(times);
        }

        return new { Subjects = subjects.Count, Rounds = 20, MicrosecondsPerSubjectCheck = perCheck };
    }

    private static long[] Sample(long[] from, int count, Random random)
    {
        var copy = from.ToArray();
        random.Shuffle(copy);
        return copy[..count];
    }

    private static double Saved(double before, double after) => Math.Round(100 * (before - after) / before, 1);

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
