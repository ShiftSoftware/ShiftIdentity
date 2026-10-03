using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Core.DataLevelAccess;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Data.Authorization;
using ShiftSoftware.TypeAuth.Core;
using Claims = ShiftSoftware.ShiftEntity.Core.Constants;

namespace ShiftIdentity.ReverseAccess.Benchmarks;

internal static class SlimParityChecks
{
    public static object Run(IHashIdService hashIds,Type[] roots)
    {
        Console.WriteLine("Slim: checking deterministic semantic and refresh fixtures.");
        var policies=new List<DataLevelAccessBuilder<ScopeRow>> {ScopeRow.Builder(),ScopeRow.Builder(true)};
        var replaced=ScopeRow.Builder();var explicitDeclaration=new DataLevelAccessBuilder<ScopeRow>();
        explicitDeclaration.On(ShiftIdentityActions.DataLevelAccess.Companies).Keys(r=>r.CompanyID,r=>r.AlternateCompanyID)
            .HashId<CompanyDTO>().Self(Claims.CompanyIdClaim);
        // Invoke the actual repository composition method; avoid duplicating its override algorithm.
        typeof(DataLevelAccessBuilder<ScopeRow>).GetMethod("ApplyOverridesFrom",BindingFlags.Instance|BindingFlags.NonPublic)!
            .Invoke(replaced,[explicitDeclaration]);policies.Add(replaced);
        var custom=ScopeRow.Builder();var customOverride=new DataLevelAccessBuilder<ScopeRow>();
        customOverride.On(ShiftIdentityActions.DataLevelAccess.Companies)
            .Match(ids=>r=>ids.Contains(r.CompanyID)||ids.Contains(r.AlternateCompanyID)).HashId<CompanyDTO>().Self(Claims.CompanyIdClaim);
        typeof(DataLevelAccessBuilder<ScopeRow>).GetMethod("ApplyOverridesFrom",BindingFlags.Instance|BindingFlags.NonPublic)!
            .Invoke(custom,[customOverride]);policies.Add(custom);
        var owner=new DataLevelAccessBuilder<ScopeRow>();owner.OnOwner(ClaimTypes.NameIdentifier).Key(r=>r.OwnerID).HashId<UserDTO>();policies.Add(owner);
        var unscoped=new DataLevelAccessBuilder<ScopeRow>();unscoped.Unscoped();policies.Add(unscoped);
        var noSelf=new DataLevelAccessBuilder<ScopeRow>();noSelf.On(ShiftIdentityActions.DataLevelAccess.Companies).Key(r=>r.CompanyID).HashId<CompanyDTO>();policies.Add(noSelf);
        var manifest=policies.SelectMany(p=>p.Dimensions).ToArray();
        var compiled=policies.Select(p=>new DataLevelAccessPolicy<ScopeRow>(p)).ToArray();
        var row=new ScopeRow {CompanyID=4,CompanyBranchID=8,RegionID=9,CountryID=10,CityID=7,BrandID=3,TeamID=12,OwnerID=1};
        ScopeRow[] rows=[row,row with{TeamID=13},row with{CompanyID=99,AlternateCompanyID=4},row with{RegionID=99},
            row with{BrandID=99},row with{CityID=99},row with{CompanyBranchID=99},row with{CountryID=99},row with{OwnerID=2},
            row with{CompanyID=null},new ScopeRow()];
        int comparisons=0;var passed=new List<string>();
        string[] variants=["merged-self","two-memberships","duplicate-self-grants","root-inheritance","dimension-inheritance",
            "empty-child-additive","explicit-and-self","null-entry","read","write","delete","maximum","no-grants","missing-country"];
        foreach(var variant in variants)
        {
            var d=ParityChecks.Seed(hashIds);
            switch(variant)
            {
                case "two-memberships":d.Memberships.Add(new(){id="201",UserID=1,TeamID=13});break;
                case "duplicate-self-grants":d.Assignments.Add(new(){id="101",UserID=1,AccessTreeID=20});break;
                case "root-inheritance":d.Users[0].AccessTree="""{"ShiftIdentityActions":["Read"]}""";break;
                case "dimension-inheritance":d.Trees[0].Tree="""{"ShiftIdentityActions":{"DataLevelAccess":["Read"]}}""";break;
                case "empty-child-additive":d.Users[0].AccessTree="""{"ShiftIdentityActions":{"Users":["Read"],"DataLevelAccess":{"Companies":[]}}}""";break;
                case "explicit-and-self":SetCompanyGrant(d,hashIds.Encode<CompanyDTO>(99));break;
                case "null-entry":SetCompanyGrant(d,TypeAuthContext.EmptyOrNullKey);break;
                case "read":case "write":case "delete":case "maximum":
                    var access=char.ToUpperInvariant(variant[0])+variant[1..];
                    d.Users[0].AccessTree=JsonSerializer.Serialize(new{ShiftIdentityActions=new[]{access}});d.Trees.Clear();d.Assignments.Clear();break;
                case "no-grants":d.Users[0].AccessTree="{}";d.Trees.Clear();d.Assignments.Clear();break;
                case "missing-country":d.Users[0].CountryID=null;break;
            }
            var source=new MemorySource(d);var lookup=new IdentityReverseAccessLookup(source,source,hashIds);
            foreach(var managed in new[]{false,true})
            {
                var batches=new[]{SlimMeasurements.Build(lookup,hashIds,roots,ShiftIdentityActions.Users,managed,manifest,false),
                    SlimMeasurements.Build(lookup,hashIds,[typeof(ShiftIdentityActions)],ShiftIdentityActions.Users,managed,manifest,false),
                    SlimMeasurements.Build(lookup,hashIds,[typeof(ShiftIdentityActions)],ShiftIdentityActions.Users,managed,manifest,true)};
                foreach(var policy in compiled)foreach(var testRow in rows)foreach(var op in PreparedSubject.Operations)
                {
                    var expected=SlimMeasurements.Evaluate(batches[0],testRow,op,policy);
                    foreach(var batch in batches.Skip(1))
                    {Require(expected.SequenceEqual(SlimMeasurements.Evaluate(batch,testRow,op,policy)));comparisons++;}
                }
                // Independent expectations ensure differential checks are not all equal denials.
                if(variant=="merged-self")
                {Require(SlimMeasurements.Evaluate(batches[2],row,Access.Read,compiled[1]).Length==1);
                    Require(SlimMeasurements.Evaluate(batches[2],row with{RegionID=99},Access.Read,compiled[0]).Length==0);
                    Require(SlimMeasurements.Evaluate(batches[2],row with{CityID=99,TeamID=99,CountryID=99},Access.Read,compiled[0]).Length==1);
                    Require(SlimMeasurements.Evaluate(batches[2],row with{CompanyID=99,AlternateCompanyID=4},Access.Read,compiled[2]).Length==1);
                    Require(SlimMeasurements.Evaluate(batches[2],row with{CompanyID=99,AlternateCompanyID=4},Access.Read,compiled[3]).Length==1);}
                if(variant=="two-memberships")Require(SlimMeasurements.Evaluate(batches[2],row with{TeamID=13},Access.Read,compiled[1]).Length==1);
                if(variant=="null-entry")Require(SlimMeasurements.Evaluate(batches[2],row with{CompanyID=null},Access.Read,compiled[0]).Length==1);
                if(managed&&new[]{"read","write","delete","maximum"}.Contains(variant))
                    foreach(var op in PreparedSubject.Operations)
                        Require(SlimMeasurements.Evaluate(batches[2],row,op,compiled[0]).Length==(op.ToString().Equals(variant,StringComparison.OrdinalIgnoreCase)?1:0));
            }
            passed.Add(variant);
        }
        var missing=new List<string>();
        foreach(var field in new[]{"active-state","company-id","company-document","branch-id","branch-document","region-id","branch-city","named-tree","tree-content","malformed-tree"})
        {
            var d=ParityChecks.Seed(hashIds);
            switch(field)
            {
                case "active-state":d.Users[0].IsActive=null;break;
                case "company-id":d.Users[0].CompanyID=null;break;
                case "company-document":d.Companies.Clear();break;
                case "branch-id":d.Users[0].CompanyBranchID=null;break;
                case "branch-document":d.Branches.Clear();break;
                case "region-id":d.Users[0].RegionID=null;break;
                case "branch-city":d.Branches[0].CityID=null;break;
                case "named-tree":d.Trees.Clear();break;
                case "tree-content":d.Trees[0].Tree="";break;
                case "malformed-tree":d.Trees[0].Tree="{";break;
            }
            var source=new MemorySource(d);var lookup=new IdentityReverseAccessLookup(source,source,hashIds);
            var failures=new List<Type?>();
            foreach(var arm in Enumerable.Range(0,3))
            {try{SlimMeasurements.Build(lookup,hashIds,arm==0?roots:[typeof(ShiftIdentityActions)],ShiftIdentityActions.Users,false,manifest,arm==2);failures.Add(null);}
                catch(Exception ex){failures.Add(ex.GetType());}}
            Require(failures[0] is not null&&failures.All(f=>f==failures[0]));missing.Add(field);
        }
        var refresh=RefreshChecks(hashIds,roots,manifest,row,compiled[1]);
        ManifestChecks(hashIds);
        return new {DifferentialAudienceComparisons=comparisons,GrantFixtures=passed,MissingInputFailClosed=missing,
            EffectivePolicies="Actual standard profile with SSC disables and seven dimensions; actual ApplyOverridesFrom composition for OR Keys and Match; owner claim, unscoped, same action with and without Self.",
            Refresh=refresh,ManifestUnknownActionAndNullSelfRejected=true};
    }

    private static object RefreshChecks(IHashIdService hashIds,Type[] roots,DataLevelDimension<ScopeRow>[] manifest,ScopeRow row,DataLevelAccessPolicy<ScopeRow> policy)
    {
        var changes=new[]{"direct-grant","named-grant","assignment","membership","disabled","deleted","company-scope","branch-city-scope","entitlement-revoked"};
        int comparisons=0;
        foreach(var change in changes)foreach(var managed in new[]{false,true})
        {
            var d=ParityChecks.Seed(hashIds);var source=new MemorySource(d);var lookup=new IdentityReverseAccessLookup(source,source,hashIds);
            var old=SlimMeasurements.Build(lookup,hashIds,[typeof(ShiftIdentityActions)],ShiftIdentityActions.Users,managed,manifest,true);
            Require(SlimMeasurements.Evaluate(old,row,Access.Read,policy).Length==1);
            d.Users[0]=JsonSerializer.Deserialize<ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels.UserModel>(JsonSerializer.Serialize(d.Users[0]))!;
            switch(change)
            {
                case "direct-grant":d.Users[0].AccessTree="{}";break;
                case "named-grant":d.Trees[0]=new(){id="20",Tree="{}"};break;
                case "assignment":d.Assignments.Clear();break;
                case "membership":d.Memberships.Clear();break;
                case "disabled":d.Users[0].IsActive=false;break;
                case "deleted":d.Users[0].IsDeleted=true;break;
                case "company-scope":d.Users[0].CompanyID=5;break;
                case "branch-city-scope":d.Branches[0]=new(){id="8",CityID=99};break;
                case "entitlement-revoked":d.Users.Clear();break; // Candidate resolver removes a revoked entitlement/group member.
            }
            Measurements.Refresh(source).GetAwaiter().GetResult();
            var full=SlimMeasurements.Build(lookup,hashIds,roots,ShiftIdentityActions.Users,managed,manifest,false);
            var narrowed=SlimMeasurements.Build(lookup,hashIds,[typeof(ShiftIdentityActions)],ShiftIdentityActions.Users,managed,manifest,false);
            var compact=SlimMeasurements.Build(lookup,hashIds,[typeof(ShiftIdentityActions)],ShiftIdentityActions.Users,managed,manifest,true);
            foreach(var op in PreparedSubject.Operations)
            {
                var expected=SlimMeasurements.Evaluate(full,row,op,policy);
                Require(expected.SequenceEqual(SlimMeasurements.Evaluate(narrowed,row,op,policy)));
                Require(expected.SequenceEqual(SlimMeasurements.Evaluate(compact,row,op,policy)));comparisons+=2;
            }
            Require(SlimMeasurements.Evaluate(old,row,Access.Read,policy).Length==1);
            Require(SlimMeasurements.Evaluate(compact,row,Access.Read,policy).Length==(managed&&change=="direct-grant"?1:0));
        }
        return new {Changes=changes,AudienceComparisons=comparisons,OldSnapshotsStayStaleUntilRebuilt=true,
            ContractDistinction="Revoking the UI grant denies UI-gated delivery but preserves assumed managed entitlement. Removing a candidate entitlement or group membership is simulated by candidate removal; no entitlement store implemented."};
    }

    private static void ManifestChecks(IHashIdService hashIds)
    {
        var d=ParityChecks.Seed(hashIds);var source=new MemorySource(d);
        var subject=new IdentityReverseAccessLookup(source,source,hashIds).GetSubjectsAsync([typeof(ShiftIdentityActions)]).GetAwaiter().GetResult()[0];
        var snapshot=new ScopeProjection(subject.TypeAuth,subject.GetUser(),ScopeRow.Builder().Dimensions);
        int rejected=0;
        try{snapshot.GetByAccess(ShiftIdentityActions.DataLevelAccess.Teams,[]);}catch(InvalidOperationException){rejected++;}
        try{snapshot.GetByAccess(ShiftIdentityActions.DataLevelAccess.Brands,null);}catch(InvalidOperationException){rejected++;}
        Require(rejected==2);
    }
    private static void SetCompanyGrant(Dataset d,string key)
    {
        var tree=JsonNode.Parse(d.Trees[0].Tree!)!;
        tree["ShiftIdentityActions"]!["DataLevelAccess"]!["Companies"]![key]=new JsonArray("Read");
        d.Trees[0].Tree=tree.ToJsonString();
    }
    private static void Require(bool condition, [System.Runtime.CompilerServices.CallerLineNumber] int line=0)
    {if(!condition){Console.Error.WriteLine($"Semantic assertion failed at harness line {line}.");throw new InvalidOperationException("Slim semantic parity failed.");}}
}
