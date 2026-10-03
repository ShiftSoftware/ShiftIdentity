using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Core.DataLevelAccess;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Country;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;
using ShiftSoftware.ShiftIdentity.Core.DTOs.City;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Brand;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Team;
using ShiftSoftware.ShiftIdentity.Data.Authorization;
using ShiftSoftware.ShiftIdentity.Data.IdentityReference;
using ShiftSoftware.TypeAuth.Core;
using ShiftSoftware.TypeAuth.Core.Actions;
using Claims = ShiftSoftware.ShiftEntity.Core.Constants;

namespace ShiftIdentity.ReverseAccess.Benchmarks;

internal static class Measurements
{
    internal sealed record Row(long? Company, long? Branch, long? Region, long? Country, long? City, long? Brand, long? Team);
    private static readonly Access[] Operations = [Access.Read, Access.Write, Access.Delete];
    private static IReadOnlyList<IdentityAccessSubject>? retainedOld, retainedNew;
    private static int sink;

    internal static DataLevelAccessPolicy<Row> Policy(bool complex)
    {
        var b = new DataLevelAccessBuilder<Row>();
        b.On(ShiftIdentityActions.DataLevelAccess.Companies).Key(r => r.Company).HashId<CompanyDTO>().Self(Claims.CompanyIdClaim);
        b.On(ShiftIdentityActions.DataLevelAccess.Branches).Key(r => r.Branch).HashId<CompanyBranchDTO>().Self(Claims.CompanyBranchIdClaim);
        if (complex)
        {
            b.On(ShiftIdentityActions.DataLevelAccess.Regions).Key(r => r.Region).HashId<RegionDTO>().Self(Claims.RegionIdClaim);
            b.On(ShiftIdentityActions.DataLevelAccess.Countries).Key(r => r.Country).HashId<CountryDTO>().Self(Claims.CountryIdClaim);
            b.On(ShiftIdentityActions.DataLevelAccess.Cities).Key(r => r.City).HashId<CityDTO>().Self(Claims.CityIdClaim);
            b.On(ShiftIdentityActions.DataLevelAccess.Brands).Key(r => r.Brand).HashId<BrandDTO>();
            b.On(ShiftIdentityActions.DataLevelAccess.Teams).Key(r => r.Team).HashId<TeamDTO>().SelfMany(Claims.TeamIdsClaim);
        }
        return new(b);
    }

    public static async Task<object> Run(Dataset data, IHashIdService hashIds, Type[] actions, Dictionary<string,string> arguments)
    {
        var iterations = int.Parse(arguments.GetValueOrDefault("--iterations", "60"));
        if(iterations<1)throw new ArgumentOutOfRangeException(nameof(iterations));
        var scales = arguments.GetValueOrDefault("--scales", "1000,5000").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).Where(x=>x>0);
        ActionBase systemAction = ShiftIdentityActions.Users;
        if (arguments.TryGetValue("--system-action", out var requested))
        {
            var parts = requested.Split(':');
            systemAction = (ActionBase)actions.Single(t => t.FullName == parts[0]).GetField(parts[1], BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        }
        Func<IdentityAccessSubject, Access, bool> system = (s,a) => s.TypeAuth.Can(systemAction,a);
        var results = new List<object> { await Scenario(arguments.ContainsKey("--bacpac")?"local-backup-projection":"local-sql-projection", data, hashIds, actions, system, iterations) };
        foreach (var scale in scales)
            results.Add(await Scenario("synthetic-repeated-shape", Scale(data, scale), hashIds, actions, system, Math.Max(12,iterations/3)));
        return new { Method = "Release, real lookup/cache; read-only source projections replayed in memory, no Cosmos I/O. Closed-loop concurrency; latency excludes queueing before a worker starts. Five construction warmups. Percentiles use nearest rank; low-sample p99 is descriptive only. Forced GC between phases; not between individual lookups. Approximate memory peaks sampled every 10 ms; sampler overhead included in process allocation/CPU.",
            DocumentCache = "One reused production source cache, TTL zero (source lifetime); documents preloaded. B retains subjects experimentally in the harness only.",
            AllocationMethod="Per-worker-thread action deltas, asserting that the action completes on the same thread. Process allocation deltas include sampling overhead and are suppressed if the counter decreases.",
            Scenarios = results, IdleExpiry = IdleExpiry(data), RefreshParity = await ParityChecks.Run(hashIds, actions) };
    }

    private static async Task<object> Scenario(string kind, Dataset data, IHashIdService hashIds, Type[] actions,
        Func<IdentityAccessSubject,Access,bool> system, int iterations)
    {
        Console.WriteLine($"Measuring {kind}: {data.Users.Count} users.");
        var source = new MemorySource(data);
        var lookup = new IdentityReverseAccessLookup(source,source,hashIds);
        var simple = Policy(false); var complex = Policy(true);
        var branches = data.Branches.ToDictionary(b => long.Parse(b.id));
        var records = data.Users.Where(u => u.IsActive == true && !u.IsDeleted).Take(64)
            .Select((u,i) => new Row(u.CompanyID,u.CompanyBranchID,u.RegionID,u.CountryID,
                branches[u.CompanyBranchID!.Value].CityID, i%3+1, data.Memberships.FirstOrDefault(m => m.UserID.ToString()==u.id)?.TeamID)).ToArray();
        var timing = new List<object>();
        timing.Add(await Measure("cold-local-prepare", 1, 1, async _ => { sink = (await lookup.GetSubjectsAsync(actions)).Count; }));
        for(var i=0;i<5;i++) sink = (await lookup.GetSubjectsAsync(actions)).Count;
        timing.Add(await Measure("A-prepare-warm-documents", iterations, 1, async _ => { sink = (await lookup.GetSubjectsAsync(actions)).Count; }));
        var firstUse = new List<double>(); long firstUseAllocated=0;
        for(var i=0;i<Math.Min(iterations,30);i++)
        {
            var fresh = await lookup.GetSubjectsAsync(actions);
            var allocated=GC.GetAllocatedBytesForCurrentThread(); var timer=Stopwatch.StartNew();
            sink=Evaluate(fresh, records[i%records.Length], Operations[i%3], simple, system).Length;
            firstUse.Add(timer.Elapsed.TotalMilliseconds); firstUseAllocated+=GC.GetAllocatedBytesForCurrentThread()-allocated;
        }
        timing.Add(new { Name="first-evaluation-new-context", Samples=firstUse.Count, LatencyMs=Distribution(firstUse), AllocatedBytesPerLookup=firstUseAllocated/firstUse.Count });
        var prepared = await lookup.GetSubjectsAsync(actions);
        int parityCases=0; var audienceSizes=new List<int>();
        foreach(var policy in new[]{simple,complex})
            for(var i=0;i<Math.Min(records.Length,16);i++)
                foreach(var operation in Operations)
                {
                    var expected=await lookup.FindUsersAsync(records[i],operation,policy,system,actions);
                    var reused=Evaluate(prepared,records[i],operation,policy,system);
                    if(!expected.Order().SequenceEqual(reused.Order())) throw new InvalidOperationException("Audience parity failed.");
                    audienceSizes.Add(reused.Length); parityCases++;
                }
        var readsBefore=source.Reads;
        foreach(var concurrency in new[]{1,4,8})
        {
            timing.Add(await Measure($"A-lookup-two-dimensions-c{concurrency}",iterations,concurrency,async i =>
                { sink=(await lookup.FindUsersAsync(records[i%records.Length],Operations[i%3],simple,system,actions)).Count; }));
            timing.Add(await Measure($"B-reused-two-dimensions-c{concurrency}",Math.Max(iterations,240),concurrency,i =>
                { sink=Evaluate(prepared,records[i%records.Length],Operations[i%3],simple,system).Length; return Task.CompletedTask; }));
        }
        timing.Add(await Measure("A-lookup-seven-dimensions",iterations,1,async i =>
            { sink=(await lookup.FindUsersAsync(records[i%records.Length],Operations[i%3],complex,system,actions)).Count; }));
        timing.Add(await Measure("B-reused-seven-dimensions",Math.Max(iterations,240),1,i =>
            { sink=Evaluate(prepared,records[i%records.Length],Operations[i%3],complex,system).Length; return Task.CompletedTask; }));
        var unexpectedLoads = source.Reads-readsBefore;
        retainedOld=prepared;
        timing.Add(await Measure("B-refresh-all-inputs-and-rebuild",3,1,async _ =>
        {
            await Refresh(source); retainedNew=await lookup.GetSubjectsAsync(actions);
            Volatile.Write(ref retainedOld,retainedNew);
        }));
        var overlapTiming=await ConcurrentRefresh(lookup,source,actions,records,complex,system);
        retainedOld=null; retainedNew=null; prepared=null!;
        var memory=Retention(lookup,actions,records,complex,system);
        GC.KeepAlive(source);
        return new { Kind=kind, Dataset=data.Shape(), RecordCount=records.Length, Operations="Read,Write,Delete", PolicyDimensions=new[]{2,7},
            ParityCases=parityCases, AudienceSize=Distribution(audienceSizes.Select(x=>(double)x)), UnexpectedWarmSourceLoads=unexpectedLoads,
            Timing=timing, ConcurrentRefresh=overlapTiming, Memory=memory };
    }

    internal static string[] Evaluate(IReadOnlyList<IdentityAccessSubject> subjects, Row row, Access operation, DataLevelAccessPolicy<Row> policy,
        Func<IdentityAccessSubject,Access,bool> system)
        => subjects.Where(s=>s.CanAccess(row,operation,policy,system)).Select(s=>s.UserId).Distinct(StringComparer.Ordinal).ToArray();

    internal static async Task Refresh(MemorySource source)
    {
        await source.RefreshAsync(IdentityReferenceFamily.Users);
        await source.RefreshAsync(IdentityReferenceFamily.Companies);
        await source.RefreshAsync(IdentityReferenceFamily.CompanyBranches);
        await source.RefreshAuthorizationAsync();
    }

    internal static async Task<object> Measure(string name,int count,int concurrency,Func<int,Task> action)
    {
        Collect(); using var process=Process.GetCurrentProcess();
        var cpu=process.TotalProcessorTime; var allocated=GC.GetTotalAllocatedBytes(true);
        var collections=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();
        var latency=new double[count]; var next=-1;long actionAllocated=0;
        long peakManaged=GC.GetTotalMemory(false), peakWorking=process.WorkingSet64, peakPrivate=process.PrivateMemorySize64;
        using var stop=new CancellationTokenSource();
        var sampler=Task.Run(async()=>
        {
            while(!stop.IsCancellationRequested)
            {
                process.Refresh(); peakManaged=Math.Max(peakManaged,GC.GetTotalMemory(false));
                peakWorking=Math.Max(peakWorking,process.WorkingSet64); peakPrivate=Math.Max(peakPrivate,process.PrivateMemorySize64);
                try { await Task.Delay(10,stop.Token); } catch(OperationCanceledException) { break; }
            }
        });
        var wall=Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0,concurrency).Select(_=>Task.Run(async()=>
        {
            int i;
            while((i=Interlocked.Increment(ref next))<count)
            {
                var thread=Environment.CurrentManagedThreadId;var before=GC.GetAllocatedBytesForCurrentThread();
                var started=Stopwatch.GetTimestamp();await action(i);latency[i]=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if(thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Allocation sample crossed threads.");
                Interlocked.Add(ref actionAllocated,GC.GetAllocatedBytesForCurrentThread()-before);
            }
        })));
        wall.Stop(); stop.Cancel(); await sampler; process.Refresh();
        var processAllocated=GC.GetTotalAllocatedBytes(true)-allocated;
        return new { Name=name,Samples=count,Concurrency=concurrency,LatencyMs=Distribution(latency),WallMs=wall.Elapsed.TotalMilliseconds,
            LookupsPerSecond=count/wall.Elapsed.TotalSeconds,CpuMs=(process.TotalProcessorTime-cpu).TotalMilliseconds,
            AllocatedBytesPerLookup=actionAllocated/count,
            ProcessAllocatedBytesPerLookup=processAllocated<0?(long?)null:processAllocated/count,
            ProcessAllocationCounterDecreased=processAllocated<0,
            GenCollections=Enumerable.Range(0,3).Select(i=>GC.CollectionCount(i)-collections[i]).ToArray(),
            SampledPeakManagedBytes=peakManaged,SampledPeakWorkingSetBytes=peakWorking,SampledPeakPrivateBytes=peakPrivate };
    }

    private static async Task<object> ConcurrentRefresh(IdentityReverseAccessLookup lookup, MemorySource source, Type[] actions,Row[] rows,
        DataLevelAccessPolicy<Row> policy,Func<IdentityAccessSubject,Access,bool> system)
    {
        using var ready=new CountdownEvent(8);using var start=new ManualResetEventSlim();
        var done=0;var counts=new int[8];var durations=new List<double>[8];
        var readers=Enumerable.Range(0,8).Select(worker=>Task.Factory.StartNew(()=>
        {
            durations[worker]=[];ready.Signal();start.Wait();
            do
            {
                var i=counts[worker]++;var timer=Stopwatch.GetTimestamp();
                sink=Evaluate(Volatile.Read(ref retainedOld)!,rows[i%rows.Length],Operations[i%3],policy,system).Length;
                durations[worker].Add(Stopwatch.GetElapsedTime(timer).TotalMilliseconds);
            }while(Volatile.Read(ref done)==0);
        },CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default)).ToArray();
        ready.Wait();var timer=Stopwatch.StartNew();start.Set();
        await Refresh(source);var replacement=await lookup.GetSubjectsAsync(actions);Volatile.Write(ref retainedOld,replacement);
        timer.Stop();Volatile.Write(ref done,1);await Task.WhenAll(readers);
        return new { Readers=8,ReadCount=counts.Sum(),RebuildMs=timer.Elapsed.TotalMilliseconds,
            ReadLatencyMs=Distribution(durations.SelectMany(x=>x)),Method="Eight dedicated readers run throughout one rebuild; one atomic reference publication. Same unchanged authorization inputs." };
    }

    // Avoid retaining an awaiter's completed Task/result on the memory-measurement stack.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PrepareRetained(IdentityReverseAccessLookup lookup,Type[] actions,bool old)
    {
        var result=lookup.GetSubjectsAsync(actions).GetAwaiter().GetResult();
        if(old)retainedOld=result;else retainedNew=result;
    }

    private static object Retention(IdentityReverseAccessLookup lookup,Type[] actions,Row[] rows,DataLevelAccessPolicy<Row> policy,
        Func<IdentityAccessSubject,Access,bool> system)
    {
        retainedOld=null; retainedNew=null; var baseline=Collect();
        PrepareRetained(lookup,actions,true); var raw=Collect();
        foreach(var row in rows) foreach(var op in Operations) sink=Evaluate(retainedOld!,row,op,policy,system).Length;
        var warmed=Collect(); PrepareRetained(lookup,actions,false);
        foreach(var row in rows) foreach(var op in Operations) sink=Evaluate(retainedNew!,row,op,policy,system).Length;
        var overlap=Collect(); retainedOld=null; var replaced=Collect(); retainedNew=null; var released=Collect();
        return new { BaselineManagedBytes=baseline,PreparedRetainedBytes=raw-baseline,WarmedRetainedBytes=warmed-baseline,
            OldAndNewOverlapBytes=overlap-baseline,AfterOldReleasedBytes=replaced-baseline,AfterAllReleasedBytes=released-baseline };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CaptureRoster(MemorySource source) => new(source.GetUsersAsync().GetAwaiter().GetResult());
    private static object IdleExpiry(Dataset data)
    {
        long now=0; var cache=new IdentityReferenceCache(TimeSpan.FromSeconds(1),()=>now); var source=new MemorySource(data,cache);
        var old=CaptureRoster(source); Collect(); var before=old.IsAlive;
        now=2000; Collect(); var expiredButIdle=old.IsAlive;
        var replacement=CaptureRoster(source); Collect(); var afterReload=old.IsAlive;
        GC.KeepAlive(source); GC.KeepAlive(cache); GC.KeepAlive(replacement);
        return new { Clock="injected monotonic clock advanced beyond 1-second TTL; forced full GC", BeforeExpiryRetained=before,
            AfterExpiryWithoutReadRetained=expiredButIdle, OldRosterAfterLazyReloadRetained=afterReload,
            Scope="Weak reference tracks cached roster dictionary. Input documents remain rooted by the benchmark dataset." };
    }

    private static long Collect() { GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();return GC.GetTotalMemory(true); }
    private static object Distribution(IEnumerable<double> values)
    {
        var a=values.Order().ToArray(); double P(double p)=>a[Math.Clamp((int)Math.Ceiling(a.Length*p)-1,0,a.Length-1)];
        return new { Min=a[0],P50=P(.5),P95=P(.95),P99=P(.99),Max=a[^1] };
    }
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static Dataset Scale(Dataset original,int count)
    {
        var d=new Dataset(); d.Trees.AddRange(original.Trees.Select(Copy)); d.Companies.AddRange(original.Companies.Select(Copy)); d.Branches.AddRange(original.Branches.Select(Copy));
        var active=original.Users.Where(u=>u.IsActive==true&&!u.IsDeleted).ToArray(); long assignmentId=1,membershipId=1;
        for(var i=0;i<count;i++)
        {
            var from=active[i%active.Length]; var user=Copy(from);user.id=(1000000L+i).ToString();d.Users.Add(user);
            foreach(var assignment in original.Assignments.Where(a=>a.UserID.ToString()==from.id))
            {var copy=Copy(assignment);copy.id=(assignmentId++).ToString();copy.UserID=long.Parse(user.id);d.Assignments.Add(copy);}
            for(var j=0;j<i%9;j++)d.Memberships.Add(new TeamUserModel{id=(membershipId++).ToString(),UserID=long.Parse(user.id),TeamID=(i+j)%64+1});
        }
        return d;
    }
}
