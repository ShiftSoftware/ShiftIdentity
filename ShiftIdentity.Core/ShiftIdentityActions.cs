using ShiftSoftware.TypeAuth.Core;
using ShiftSoftware.TypeAuth.Core.Actions;

namespace ShiftSoftware.ShiftIdentity.Core;

[ActionTree("Identity", "Users, Access, and Organization")]
public class ShiftIdentityActions
{
    public readonly static ReadWriteDeleteAction Apps = new ReadWriteDeleteAction("Apps");
    public readonly static ReadWriteDeleteAction AccessTrees = new ReadWriteDeleteAction("Access Trees");
    public readonly static ReadWriteDeleteAction Users = new ReadWriteDeleteAction("Users");
    public readonly static ReadWriteDeleteAction UserGroups = new ReadWriteDeleteAction("User Groups");

    public readonly static ReadWriteDeleteAction Regions = new ReadWriteDeleteAction("Regions");
    public readonly static ReadWriteDeleteAction Brands = new ReadWriteDeleteAction("Brands");
    public readonly static ReadWriteDeleteAction Cities = new ReadWriteDeleteAction("Cities");
    public readonly static ReadWriteDeleteAction Departments = new ReadWriteDeleteAction("Departments");
    public readonly static ReadWriteDeleteAction Services = new ReadWriteDeleteAction("Services");
    public readonly static ReadWriteDeleteAction Companies = new ReadWriteDeleteAction("Companies");
    public readonly static ReadWriteDeleteAction CompanyBranches = new ReadWriteDeleteAction("Company Branches");

    [ActionTree("Data Level Access", "Data Level or Row-Level Access")]
    public class DataLevelAccess
    {
        public readonly static DynamicReadWriteDeleteAction Regions = new DynamicReadWriteDeleteAction("Regions");
        public readonly static DynamicReadWriteDeleteAction Companies = new DynamicReadWriteDeleteAction("Companies");
        public readonly static DynamicReadWriteDeleteAction Branches = new DynamicReadWriteDeleteAction("Branches");
        public readonly static DynamicReadWriteDeleteAction UserGroups = new DynamicReadWriteDeleteAction("User Groups");
    }
}
