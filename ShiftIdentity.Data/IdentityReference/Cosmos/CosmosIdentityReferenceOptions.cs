using ShiftSoftware.ShiftEntity.Model.Replication;

namespace ShiftSoftware.ShiftIdentity.Data.IdentityReference.Cosmos;

/// <summary>
/// Database and container names for <see cref="CosmosIdentityReferenceSource{TCosmosClient}"/>.
///
/// <para>Deliberately Cosmos-named: these are Cosmos concepts and belong to that backend, not to
/// <see cref="IIdentityReferenceSource"/>. Another backend brings its own options type.</para>
///
/// <para>Every value defaults to the shared <see cref="IdentityDatabaseAndContainerNames"/> constants, so a
/// host that has not renamed anything configures nothing.</para>
/// </summary>
public class CosmosIdentityReferenceOptions
{
    public string DatabaseName { get; set; } = IdentityDatabaseAndContainerNames.DatabaseName;

    public string CountryContainerName { get; set; } = IdentityDatabaseAndContainerNames.CountryContainerName;

    public string CompanyContainerName { get; set; } = IdentityDatabaseAndContainerNames.CompanyContainerName;

    public string CompanyBranchContainerName { get; set; } = IdentityDatabaseAndContainerNames.CompanyBranchContainerName;

    public string ServiceContainerName { get; set; } = IdentityDatabaseAndContainerNames.ServiceContainerName;

    public string DepartmentContainerName { get; set; } = IdentityDatabaseAndContainerNames.DepartmentContainerName;

    public string TeamContainerName { get; set; } = IdentityDatabaseAndContainerNames.TeamContainerName;

    public string BrandContainerName { get; set; } = IdentityDatabaseAndContainerNames.BrandContainerName;

    public string UserContainerName { get; set; } = IdentityDatabaseAndContainerNames.UserContainerName;
}
