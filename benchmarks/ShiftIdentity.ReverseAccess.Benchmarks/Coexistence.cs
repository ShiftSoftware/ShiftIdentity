using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Data.Authorization;
using ShiftSoftware.ShiftIdentity.Data.IdentityReference;
using ShiftSoftware.ShiftIdentity.Data.IdentityReference.Cosmos;
using ShiftSoftware.TypeAuth.Core;

namespace ShiftIdentity.ReverseAccess.Benchmarks;

internal static class Coexistence
{
    private static readonly List<MemorySource> Sources=[];
    private static readonly List<IReadOnlyList<IdentityAccessSubject>> Sets=[];
    private static readonly List<WeakReference> ObservedSets=[];
    private static IdentityReferenceCache? sharedCache;
    private static int sink;
    private static Func<IdentityAccessSubject,Access,bool> SystemAccess=(s,a)=>s.TypeAuth.Can(ShiftIdentityActions.Users,a);

    public static async Task<object> Run(Dataset data,IHashIdService hashIds,Type[] actions,Dictionary<string,string> arguments)
    {
        if(arguments.TryGetValue("--system-action",out var requested))
        {
            var parts=requested.Split(':');var action=(ShiftSoftware.TypeAuth.Core.Actions.ActionBase)actions.Single(t=>t.FullName==parts[0]).GetField(parts[1])!.GetValue(null)!;
            SystemAccess=(s,a)=>s.TypeAuth.Can(action,a);
        }
        // Use a broad data predicate to exercise all seven dimensions; no final audience is cached.
        var policy=Measurements.Policy(true);var user=data.Users.First(u=>u.IsActive==true&&!u.IsDeleted);
        var row=new Measurements.Row(user.CompanyID,user.CompanyBranchID,user.RegionID,user.CountryID,
            data.Branches.Single(b=>b.id==user.CompanyBranchID.ToString()).CityID,1,1);
        var phases=new List<object>();
        Sources.Clear();Sets.Clear();ObservedSets.Clear();sharedCache=null;var baseline=Snapshot();
        sharedCache=new IdentityReferenceCache(TimeSpan.FromMinutes(5));
        AddSource(data,true);WarmDocuments(Sources[0]);
        phases.Add(new {Phase="one-document-cache",Snapshot=Snapshot(),SourceReads=Sources.Sum(s=>s.Reads)});
        Prepare(Sources[0],hashIds,actions,row);
        phases.Add(new {Phase="one-whole-population-prepared-set",Snapshot=Snapshot(),Sets=Sets.Count,SubjectsPerSet=Sets[0].Count,
            SystemAllowedByOperation=new[]{Access.Read,Access.Write,Access.Delete}.Select(op=>new {Operation=op.ToString(),Users=Sets[0].Count(s=>SystemAccess(s,op))}).ToArray()});
        if(arguments.TryGetValue("--hold-ms",out var holdText))
        {
            var held=Snapshot();var ready=new {ProcessId=Environment.ProcessId,Baseline=baseline,Held=held,PreparedSets=Sets.Count,SubjectsPerSet=Sets[0].Count};
            await File.WriteAllTextAsync(arguments["--output"]+".ready.json",JsonSerializer.Serialize(ready));
            await Task.Delay(int.Parse(holdText));
            Release();var released=Snapshot();
            return new {Mode="separate-process-worker",Baseline=baseline,Held=held,Released=released};
        }
        var sharedTimer=Stopwatch.StartNew();
        var latencies=new System.Collections.Concurrent.ConcurrentBag<double>();
        await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Task.Run(()=>
        {
            for(var i=0;i<40;i++)
            {
                var started=Stopwatch.GetTimestamp();
                sink=Measurements.Evaluate(Sets[0],row,new[]{Access.Read,Access.Write,Access.Delete}[i%3],policy,SystemAccess).Length;
                latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        })));
        var times=latencies.Order().ToArray();
        phases.Add(new {Phase="eight-readers-share-one-prepared-set",Sets=Sets.Count,Lookups=times.Length,WallMs=sharedTimer.Elapsed.TotalMilliseconds,
            P50Ms=times[(int)(times.Length*.50)-1],P95Ms=times[(int)(times.Length*.95)-1],Snapshot=Snapshot()});
        foreach(var desired in new[]{2,4})
        {
            var newSources=Enumerable.Range(Sources.Count,desired-Sources.Count).Select(_=>AddSource(data,true)).ToArray();
            var allocation=GC.GetTotalAllocatedBytes(true);var timer=Stopwatch.StartNew();
            await Task.WhenAll(newSources.Select(source=>Task.Run(()=>Prepare(source,hashIds,actions,row))));
            phases.Add(new {Phase="independent-concurrent-batches-shared-documents",Sets=Sets.Count,NewSets=newSources.Length,
                BuildWallMs=timer.Elapsed.TotalMilliseconds,BuildAllocatedBytes=GC.GetTotalAllocatedBytes(true)-allocation,Snapshot=Snapshot(),SourceReads=Sources.Sum(s=>s.Reads)});
        }
        // Four old batches still hold their sets while one replacement generation is prepared for each.
        var refreshAllocated=GC.GetTotalAllocatedBytes(true);var refreshTimer=Stopwatch.StartNew();
        await Task.WhenAll(Sources.ToArray().Select(source=>Task.Run(()=>Prepare(source,hashIds,actions,row))));
        phases.Add(new {Phase="four-old-plus-four-replacement-sets",Sets=Sets.Count,BuildWallMs=refreshTimer.Elapsed.TotalMilliseconds,
            BuildAllocatedBytes=GC.GetTotalAllocatedBytes(true)-refreshAllocated,Snapshot=Snapshot()});
        Sets.RemoveRange(0,4);
        phases.Add(new {Phase="old-generation-released",Sets=Sets.Count,Snapshot=Snapshot()});
        Sets.Clear();
        phases.Add(new {Phase="all-prepared-sets-released-documents-retained",Snapshot=Snapshot()});
        Release();var beforeDocumentComparison=Snapshot();
        sharedCache=new IdentityReferenceCache(TimeSpan.FromMinutes(5));
        for(var i=0;i<4;i++)WarmDocuments(AddSource(data,true));
        phases.Add(new {Phase="four-scopes-one-shared-document-cache",Snapshot=Snapshot(),SourceReads=Sources.Sum(s=>s.Reads)});
        Release();Snapshot();
        for(var i=0;i<4;i++)WarmDocuments(AddSource(data,false));
        phases.Add(new {Phase="four-scopes-four-private-document-caches",Snapshot=Snapshot(),SourceReads=Sources.Sum(s=>s.Reads)});
        Release();var final=Snapshot();
        return new {Method="Server GC is set externally. Existing local input stays rooted in the baseline. Each cache miss clones its documents to represent separately deserialized SDK results. Full GC before retained-memory snapshots; process working set/commit may remain reserved after release. All population sets are experimental harness roots.",
            Baseline=baseline,BeforeDocumentComparison=beforeDocumentComparison,Phases=phases,Final=final,DiOwnership=VerifyOwnership(hashIds)};
    }

    private static object VerifyOwnership(IHashIdService hashIds)
    {
        var field=typeof(CosmosIdentityReferenceSource<MemoryClient>).GetField("cache",BindingFlags.NonPublic|BindingFlags.Instance)!;
        object Check(TimeSpan ttl)
        {
            ServiceProvider Build()
            {
                var services=new ServiceCollection();services.AddSingleton(new MemoryClient());services.AddSingleton(hashIds);
                services.AddIdentityReverseAccessLookup<MemoryClient>(o=>o.CacheTimeToLive=ttl);
                return services.BuildServiceProvider();
            }
            using var provider=Build();using var a=provider.CreateScope();using var b=provider.CreateScope();using var other=Build();using var c=other.CreateScope();
            var sa=a.ServiceProvider.GetRequiredService<CosmosIdentityReferenceSource<MemoryClient>>();
            var sb=b.ServiceProvider.GetRequiredService<CosmosIdentityReferenceSource<MemoryClient>>();
            var sc=c.ServiceProvider.GetRequiredService<CosmosIdentityReferenceSource<MemoryClient>>();
            return new {Ttl=ttl.ToString(),SourceIsScoped=!ReferenceEquals(sa,sb),ReferenceAndAuthorizationUseSameSource=
                ReferenceEquals(a.ServiceProvider.GetRequiredService<IIdentityReferenceSource>(),a.ServiceProvider.GetRequiredService<IIdentityAuthorizationSource>()),
                DocumentCacheSharedAcrossScopes=ReferenceEquals(field.GetValue(sa),field.GetValue(sb)),
                DocumentCacheSharedAcrossServiceProviders=ReferenceEquals(field.GetValue(sa),field.GetValue(sc)),
                LookupIsScoped=!ReferenceEquals(a.ServiceProvider.GetRequiredService<IdentityReverseAccessLookup>(),b.ServiceProvider.GetRequiredService<IdentityReverseAccessLookup>())};
        }
        return new[]{Check(TimeSpan.Zero),Check(TimeSpan.FromMinutes(5))};
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static MemorySource AddSource(Dataset data,bool shared)
    {var source=new MemorySource(data,shared?sharedCache:null,cloneRows:true);Sources.Add(source);return source;}

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WarmDocuments(MemorySource s)
    {
        s.GetUsersAsync().GetAwaiter().GetResult();s.GetCompaniesAsync().GetAwaiter().GetResult();s.GetCompanyBranchesAsync().GetAwaiter().GetResult();
        s.GetAccessTreesAsync().GetAwaiter().GetResult();s.GetUserAccessTreesAsync().GetAwaiter().GetResult();s.GetTeamUsersAsync().GetAwaiter().GetResult();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Prepare(MemorySource source,IHashIdService hashIds,Type[] actions,Measurements.Row row)
    {
        var set=new IdentityReverseAccessLookup(source,source,hashIds).GetSubjectsAsync(actions).GetAwaiter().GetResult();
        var policy=Measurements.Policy(true);
        foreach(var op in new[]{Access.Read,Access.Write,Access.Delete})sink=Measurements.Evaluate(set,row,op,policy,SystemAccess).Length;
        lock(Sets){Sets.Add(set);ObservedSets.Add(new WeakReference(set));}
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Release(){Sets.Clear();Sources.Clear();sharedCache=null;}

    private static object Snapshot()
    {
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        using var p=Process.GetCurrentProcess();var info=GC.GetGCMemoryInfo();
        return new {ManagedBytes=GC.GetTotalMemory(true),GcCommittedBytes=info.TotalCommittedBytes,WorkingSetBytes=p.WorkingSet64,PrivateBytes=p.PrivateMemorySize64,
            PreparedListsStillAlive=ObservedSets.Count(w=>w.IsAlive)};
    }
}
