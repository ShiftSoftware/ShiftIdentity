using ShiftSoftware.ShiftEntity.Model.Replication;

namespace ShiftSoftware.ShiftIdentity.Data.Cosmos.Options;

public class IdentityReferenceCosmosDataOptions
{
    public string DatabaseName { get; set; } = IdentityDatabaseAndContainerNames.DatabaseName;

    public string CountryContainerName { get; set; } = IdentityDatabaseAndContainerNames.CountryContainerName;

    public string CompanyContainerName { get; set; } = IdentityDatabaseAndContainerNames.CompanyContainerName;

    public string CompanyBranchContainerName { get; set; } = IdentityDatabaseAndContainerNames.CompanyBranchContainerName;

    public string ServiceContainerName { get; set; } = IdentityDatabaseAndContainerNames.ServiceContainerName;

    public string DepartmentContainerName { get; set; } = IdentityDatabaseAndContainerNames.DepartmentContainerName;

    public string TeamContainerName { get; set; } = IdentityDatabaseAndContainerNames.TeamContainerName;

    public string BrandContainerName { get; set; } = IdentityDatabaseAndContainerNames.BrandContainerName;
}
