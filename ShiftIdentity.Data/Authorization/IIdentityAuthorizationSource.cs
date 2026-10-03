using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;

namespace ShiftSoftware.ShiftIdentity.Data.Authorization;

/// <summary>
/// Reads the grant and membership documents accompanying identity reference data.
/// Readers must report failed reads, not replace them with empty grant or membership sets.
/// </summary>
public interface IIdentityAuthorizationSource
{
    Task<IReadOnlyDictionary<string, AccessTreeModel>> GetAccessTreesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, UserAccessTreeModel>> GetUserAccessTreesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, TeamUserModel>> GetTeamUsersAsync(CancellationToken cancellationToken = default);
    /// <summary>Reloads these three families through to the store.</summary>
    Task RefreshAuthorizationAsync(CancellationToken cancellationToken = default);
}
