using System.Text.Json;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Brand;
using ShiftSoftware.ShiftIdentity.Data.Authorization;
using ShiftSoftware.TypeAuth.Core;

namespace ShiftIdentity.ReverseAccess.Benchmarks;

internal static class ParityChecks
{
    public static async Task<object> Run(IHashIdService hashIds, Type[] actions)
    {
        var passed=new List<string>();
        var policy=Measurements.Policy(true);
        Func<IdentityAccessSubject,Access,bool> system=(s,a)=>s.TypeAuth.Can(ShiftIdentityActions.Users,a);
        var row=new Measurements.Row(4,8,9,10,7,3,12);
        foreach(var change in new[]{"direct-grant","named-grant","assignment","membership","disabled","deleted","company-scope","branch-city-scope"})
        {
            var d=Seed(hashIds);var source=new MemorySource(d);var lookup=new IdentityReverseAccessLookup(source,source,hashIds);
            var old=await lookup.GetSubjectsAsync(actions);
            if(Measurements.Evaluate(old,row,Access.Read,policy,system).Length!=1)throw new InvalidOperationException("Parity fixture did not grant access.");
            switch(change)
            {
                case "direct-grant": d.Users[0]=Copy(d.Users[0]);d.Users[0].AccessTree="{}";break;
                case "named-grant": d.Trees[0]=Copy(d.Trees[0]);d.Trees[0].Tree="{}";break;
                case "assignment": d.Assignments.Clear();break;
                case "membership": d.Memberships.Clear();break;
                case "disabled": d.Users[0]=Copy(d.Users[0]);d.Users[0].IsActive=false;break;
                case "deleted": d.Users[0]=Copy(d.Users[0]);d.Users[0].IsDeleted=true;break;
                case "company-scope": d.Users[0]=Copy(d.Users[0]);d.Users[0].CompanyID=5;break;
                case "branch-city-scope": d.Branches[0]=Copy(d.Branches[0]);d.Branches[0].CityID=99;break;
            }
            await Measurements.Refresh(source);
            var rebuilt=await lookup.GetSubjectsAsync(actions);
            foreach(var operation in new[]{Access.Read,Access.Write,Access.Delete})
            {
                var expected=await lookup.FindUsersAsync(row,operation,policy,system,actions);
                var actual=Measurements.Evaluate(rebuilt,row,operation,policy,system);
                if(expected.Count!=0||actual.Length!=0)throw new InvalidOperationException("Refresh parity failed.");
            }
            if(Measurements.Evaluate(old,row,Access.Read,policy,system).Length!=1)
                throw new InvalidOperationException("Old prepared context unexpectedly changed.");
            passed.Add(change);
        }
        return new { Passed=passed,Checks=passed.Count*3,OldContextsRemainStaleUntilRebuilt=true,
            Coverage="Synthetic deterministic fixtures with encoded IDs, merged direct/named grants and self scope across seven dimensions; memory-only changes." };
    }

    private static T Copy<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    internal static Dataset Seed(IHashIdService hashIds)
    {
        var d=new Dataset();
        d.Users.Add(new UserModel{id="1",IsActive=true,Username="benchmark",FullName="benchmark",CompanyID=4,CompanyBranchID=8,RegionID=9,CountryID=10,
            AccessTree="""{"ShiftIdentityActions":{"Users":["Read"]}}"""});
        d.Companies.AddRange([new CompanyModel{id="4"},new CompanyModel{id="5"}]);
        d.Branches.Add(new CompanyBranchModel{id="8",CityID=7});
        var dimensions=new Dictionary<string,object>();
        foreach(var dimension in new[]{"Companies","Branches","Regions","Countries","Cities","Teams"})
            dimensions[dimension]=new Dictionary<string,object>{{TypeAuthContext.SelfReferenceKey,new[]{"Read"}}};
        dimensions["Brands"]=new Dictionary<string,object>{{hashIds.Encode<BrandDTO>(3),new[]{"Read"}}};
        d.Trees.Add(new AccessTreeModel{id="20",Name="benchmark",Tree=JsonSerializer.Serialize(new{ShiftIdentityActions=new{DataLevelAccess=dimensions}})});
        d.Assignments.Add(new UserAccessTreeModel{id="100",UserID=1,AccessTreeID=20});
        d.Memberships.Add(new TeamUserModel{id="200",UserID=1,TeamID=12});
        return d;
    }
}
