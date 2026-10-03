using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Core.DataLevelAccess;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Data.Authorization;
using ShiftSoftware.TypeAuth.Core;
using ShiftSoftware.TypeAuth.Core.Actions;

namespace ShiftIdentity.ReverseAccess.Benchmarks;

internal static class SlimMeasurements
{
    internal sealed record Batch(PreparedSubject[] Subjects, WeakReference[] Engines);
    private static Batch? oldBatch, newBatch;
    private static Batch[]? parallelBatches;
    private static int sink;

    public static async Task<object> Run(Dataset data, IHashIdService hashIds, Type[] roots, Dictionary<string,string> args)
    {
        var parts = args["--system-action"].Split(':');
        var systemRoot = roots.Single(t => t.FullName == parts[0]);
        var system = (ActionBase)systemRoot.GetField(parts[1], BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
        var iterations = int.Parse(args.GetValueOrDefault("--iterations", "24"));
        if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations));
        var builder = ScopeRow.Builder();
        var policy = new DataLevelAccessPolicy<ScopeRow>(builder);
        var active = data.Users.Where(u => u.IsActive == true && !u.IsDeleted).OrderBy(u => u.id, StringComparer.Ordinal).ToArray();
        var rows = active.Where((_, i) => i % Math.Max(1, active.Length / 48) == 0).Take(48).Select((u,i) => new ScopeRow
        {
            RegionID=u.RegionID, CompanyID=u.CompanyID, CompanyBranchID=u.CompanyBranchID, BrandID=i%3+1,
            CityID=999999, CountryID=999999, TeamID=999999,
        }).Concat([new ScopeRow(), new ScopeRow {RegionID=999999,CompanyID=999999,CompanyBranchID=999999,BrandID=999999}]).ToArray();
        var semantic = SlimParityChecks.Run(hashIds, roots);
        var scenarios = new List<object>();
        foreach (var count in new[]{1,10,50,active.Length}.Where(n => n <= active.Length).Distinct())
        {
            // Stable, evenly spaced real candidates; no duplicated or synthetic user population.
            var subset = Subset(data, Enumerable.Range(0,count).Select(i => active[i*active.Length/count].id).ToHashSet());
            var source = new MemorySource(subset);
            var lookup = new IdentityReverseAccessLookup(source,source,hashIds);
            foreach (var managed in new[]{false,true})
            {
                var required = managed ? new[]{typeof(ShiftIdentityActions)} : new[]{typeof(ShiftIdentityActions),systemRoot}.Distinct().ToArray();
                var parity = Compare(lookup, hashIds, roots, required, system, managed, builder, rows, policy);
                foreach (var arm in new[]{"full-roots","required-roots","compact-projection"})
                {
                    Console.WriteLine($"Slim: {count} candidates, {(managed?"managed-entitlement":"UI-gated")}, {arm}.");
                    var selectedRoots = arm == "full-roots" ? roots : required;
                    bool project = arm == "compact-projection";
                    Batch Prepare(bool track=false) => Build(lookup,hashIds,selectedRoots,system,managed,builder.Dimensions,project,track);
                    for(var i=0;i<3;i++) Discard(Prepare);
                    var memory = Retention(Prepare,rows,policy,project);
                    var reads = source.Reads;
                    var timings = new List<object>();
                    foreach(var concurrency in new[]{1,4})
                        timings.Add(await Measurements.Measure("construct",iterations,concurrency,_ =>
                        { Discard(Prepare); return Task.CompletedTask; }));
                    timings.AddRange(await ReadTimings(Prepare,rows,policy));
                    var overlap = await ConcurrentRefresh(Prepare,source,rows,policy);
                    scenarios.Add(new { Candidates=count, Contract=managed?"proposed-managed-entitlement":"UI-gated",
                        Approach=arm, RegisteredRoots=selectedRoots.Length, Parity=parity, Memory=memory, Timing=timings,
                        ConcurrentRefresh=overlap, SourceLoadsIncludingExplicitRefresh=source.Reads-reads });
                }
            }
        }
        return new { Method="Warm in-memory document cache; actual reverse lookup, standard profile and TypeAuth. Candidate subset applied before subject construction, full grant/reference families retained. No Cosmos I/O savings measured. Three warmups; closed-loop c1/c4 construction and c1/c4/c8 repeated lookup; per-worker allocation counters. GC-forced retention includes wrappers, weak references and runtime noise. Compact construction first builds the whole required-root candidate batch, then projects; it does not stream candidates or avoid temporary TypeAuth allocation.",
            Contracts="UI-gated: same-operation system action plus data policy. Managed-entitlement: assumes each candidate already has a current trusted topic entitlement, then applies identical data policy; no implemented entitlement/group store, payload policy or delivery flow. Both are experiments; no removal of production gates.",
            Operations="Read, Write, Delete, Maximum", Dimensions="Region, Company, Branch, Brand; actual standard profile, Country/City/Team disabled",
            Records="50 representative SSC-shaped rows (including null and out-of-scope cases); no real ticket records loaded. Brands 1..3 are illustrative.",
            Projection="Engine-resolved R/W/D/Maximum bundles with wildcard/null/hash/self semantics, minimal required claims and fixed system gate bits. No manual grant union or JSON interpretation. Manifest is captured from the effective builder. No generalized arbitrary system-predicate support.",
            ActionGraphShape=new {FullRoots=GraphShape(roots),UiRequiredRoots=GraphShape([typeof(ShiftIdentityActions),systemRoot]),
                EntitlementRequiredRoots=GraphShape([typeof(ShiftIdentityActions)])},
            SemanticParity=semantic, Scenarios=scenarios };
    }

    internal static Dataset Subset(Dataset source, HashSet<string> ids)
    {
        var d=new Dataset(); d.Users.AddRange(source.Users.Where(u=>ids.Contains(u.id)));
        d.Trees.AddRange(source.Trees);d.Assignments.AddRange(source.Assignments);d.Memberships.AddRange(source.Memberships);
        d.Companies.AddRange(source.Companies);d.Branches.AddRange(source.Branches);return d;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Batch Build(IdentityReverseAccessLookup lookup, IHashIdService hashIds, Type[] roots, ActionBase system,
        bool managed, IEnumerable<DataLevelDimension<ScopeRow>> manifest, bool project, bool track=false)
    {
        var subjects=lookup.GetSubjectsAsync(roots).GetAwaiter().GetResult();
        var weak=track?subjects.Select(s=>new WeakReference(s.TypeAuth)).ToArray():[];
        return new(subjects.Select(s=>new PreparedSubject(s,system,managed,hashIds,manifest,project)).ToArray(),weak);
    }

    internal static string[] Evaluate(Batch batch, ScopeRow row, Access op, DataLevelAccessPolicy<ScopeRow> policy)
        => batch.Subjects.Where(s=>s.Check(row,op,policy)).Select(s=>s.Id).Order(StringComparer.Ordinal).ToArray();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object Compare(IdentityReverseAccessLookup lookup,IHashIdService hashIds,Type[] roots,Type[] required,
        ActionBase system,bool managed,DataLevelAccessBuilder<ScopeRow> builder,ScopeRow[] rows,DataLevelAccessPolicy<ScopeRow> policy)
    {
        var full=Build(lookup,hashIds,roots,system,managed,builder.Dimensions,false);
        var narrow=Build(lookup,hashIds,required,system,managed,builder.Dimensions,false);
        var compact=Build(lookup,hashIds,required,system,managed,builder.Dimensions,true);
        var audiences=new List<int>();
        foreach(var row in rows) foreach(var op in PreparedSubject.Operations)
        {
            var expected=Evaluate(full,row,op,policy);
            if(!expected.SequenceEqual(Evaluate(narrow,row,op,policy))||!expected.SequenceEqual(Evaluate(compact,row,op,policy)))
                throw new InvalidOperationException("Real-candidate projection parity failed.");
            audiences.Add(expected.Length);
        }
        return new { AudienceComparisons=audiences.Count*2,NonemptyAudiences=audiences.Count(n=>n>0),
            AudienceSizes=Distribution(audiences.Select(n=>(double)n)) };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Discard(Func<bool,Batch> prepare) => sink=prepare(false).Subjects.Length;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Keep(Func<bool,Batch> prepare,bool old) { if(old)oldBatch=prepare(true);else newBatch=prepare(true); }
    private static void Warm(Batch batch,ScopeRow[] rows,DataLevelAccessPolicy<ScopeRow> policy)
    { foreach(var row in rows)foreach(var op in PreparedSubject.Operations)sink=Evaluate(batch,row,op,policy).Length; }

    private static object Retention(Func<bool,Batch> prepare,ScopeRow[] rows,DataLevelAccessPolicy<ScopeRow> policy,bool project)
    {
        oldBatch=null;newBatch=null;parallelBatches=null;var baseline=Collect();
        Keep(prepare,true);var built=Collect();
        var alive=oldBatch!.Engines.Count(w=>w.IsAlive);var tracked=oldBatch.Engines.Length;
        if(alive!=(project?0:tracked))throw new InvalidOperationException("TypeAuth retention proof failed.");
        Warm(oldBatch,rows,policy);var warm=Collect();
        Keep(prepare,false);Warm(newBatch!,rows,policy);var overlap=Collect();
        oldBatch=null;var releasedOld=Collect();newBatch=null;var releasedAll=Collect();
        HoldParallel(prepare);var four=Collect();parallelBatches=null;var releasedFour=Collect();
        return new { BaselineBytes=baseline,PreparedBytes=built-baseline,WarmedBytes=warm-baseline,
            TwoGenerationBytes=overlap-baseline,AfterOldReleasedBytes=releasedOld-baseline,AfterAllReleasedBytes=releasedAll-baseline,
            FourIndependentBatchesBytes=four-baseline,AfterFourReleasedBytes=releasedFour-baseline,
            TrackedTypeAuthContexts=tracked,LiveTypeAuthAfterForcedGc=alive };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HoldParallel(Func<bool,Batch> prepare)
        => parallelBatches=Enumerable.Range(0,4).Select(_=>Task.Run(()=>prepare(false))).ToArray().Select(t=>t.GetAwaiter().GetResult()).ToArray();

    private static async Task<List<object>> ReadTimings(Func<bool,Batch> prepare,ScopeRow[] rows,DataLevelAccessPolicy<ScopeRow> policy)
    {
        var batch=prepare(false);Warm(batch,rows,policy);var result=new List<object>();
        foreach(var mixed in new[]{false,true})foreach(var concurrency in new[]{1,4,8})
            result.Add(await Measurements.Measure(mixed?"mixed-operation-repeated-lookup":"read-repeated-lookup",400,concurrency,i =>
            { sink=Evaluate(batch,rows[i%rows.Length],mixed?PreparedSubject.Operations[i%4]:Access.Read,policy).Length;return Task.CompletedTask; }));
        return result;
    }

    private static async Task<object> ConcurrentRefresh(Func<bool,Batch> prepare,MemorySource source,ScopeRow[] rows,DataLevelAccessPolicy<ScopeRow> policy)
    {
        Keep(prepare,true);var done=0;using var ready=new CountdownEvent(8);using var start=new ManualResetEventSlim();
        var samples=new List<double>[8];
        var tasks=Enumerable.Range(0,8).Select(worker=>Task.Factory.StartNew(()=>
        {
            samples[worker]=[];ready.Signal();start.Wait();int i=0;
            do {var tick=Stopwatch.GetTimestamp();var n=i++;
                sink=Evaluate(Volatile.Read(ref oldBatch)!,rows[n%rows.Length],PreparedSubject.Operations[n%4],policy).Length;
                samples[worker].Add(Stopwatch.GetElapsedTime(tick).TotalMilliseconds);
            } while(Volatile.Read(ref done)==0);
        },CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default)).ToArray();
        ready.Wait();start.Set();var timer=Stopwatch.StartNew();
        await Measurements.Refresh(source);Volatile.Write(ref oldBatch,prepare(false));timer.Stop();
        Volatile.Write(ref done,1);await Task.WhenAll(tasks);oldBatch=null;
        return new { Readers=8,RebuildMs=timer.Elapsed.TotalMilliseconds,Lookups=samples.Sum(s=>s.Count),
            ReadLatencyMs=Distribution(samples.SelectMany(s=>s)),InputChanges=false };
    }

    internal static long Collect() {GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();return GC.GetTotalMemory(true);}
    private static object GraphShape(Type[] roots)
    {
        var context=new TypeAuthContext("{}",roots);
        static IEnumerable<ActionTreeNode> Walk(ActionTreeNode node)
        {yield return node;foreach(var child in node.ActionTreeItems)foreach(var item in Walk(child))yield return item;}
        var nodes=Walk(context.ActionTree).ToArray();
        return new {Roots=roots.Length,NodesIncludingRoot=nodes.Length,DynamicExpandedItems=nodes.Count(n=>n.IsADynamicSubItem),
            Method="Empty-grant registered action graph; counts constant root structure, not a managed heap attribution."};
    }
    private static object Distribution(IEnumerable<double> values)
    {
        var ordered=values.Order().ToArray();double P(double p)=>ordered[Math.Clamp((int)Math.Ceiling(ordered.Length*p)-1,0,ordered.Length-1)];
        return new {P50=P(.5),P95=P(.95),Max=ordered[^1]};
    }
}
