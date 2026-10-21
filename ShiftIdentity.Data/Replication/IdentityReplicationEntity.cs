namespace ShiftSoftware.ShiftIdentity.Data.Replication;

/// <summary>
/// An entity the Identity catch-up replicates. Each is one step of <see cref="IdentityCatchUpReplicationExtensions"/>,
/// named in its report as here.
/// </summary>
public enum IdentityReplicationEntity
{
    Service,
    Department,
    Brand,
    Region,
    Country,
    City,
    CompanyBranch,
    Company,
    Team,
    User,
    AccessTree,
    UserAccessTree,
    TeamUser,
}

/// <summary>The entities of each Identity catch-up, in the order it runs them.</summary>
public static class IdentityReplicationEntities
{
    /// <summary>Every entity but users and authorization: what a host that does not replicate users replicates.</summary>
    public static IReadOnlyList<IdentityReplicationEntity> Organization { get; } =
    [
        IdentityReplicationEntity.Service,
        IdentityReplicationEntity.Department,
        IdentityReplicationEntity.Brand,
        IdentityReplicationEntity.Region,
        IdentityReplicationEntity.Country,
        IdentityReplicationEntity.City,
        IdentityReplicationEntity.CompanyBranch,
        IdentityReplicationEntity.Company,
        IdentityReplicationEntity.Team,
    ];

    /// <summary>The access trees, and which users and teams hold them.</summary>
    public static IReadOnlyList<IdentityReplicationEntity> Authorization { get; } =
    [
        IdentityReplicationEntity.AccessTree,
        IdentityReplicationEntity.UserAccessTree,
        IdentityReplicationEntity.TeamUser,
    ];

    /// <summary>Every entity: the organization, then users, then authorization.</summary>
    public static IReadOnlyList<IdentityReplicationEntity> All { get; } =
        [.. Organization, IdentityReplicationEntity.User, .. Authorization];
}
