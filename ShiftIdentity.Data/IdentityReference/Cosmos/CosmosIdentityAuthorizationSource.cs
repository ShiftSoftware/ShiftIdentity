using Microsoft.Azure.Cosmos;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Data.Authorization;

namespace ShiftSoftware.ShiftIdentity.Data.IdentityReference.Cosmos;

// Authorization families use the same Cosmos paging, cache expiry and failed-load eviction as reference data.
public partial class CosmosIdentityReferenceSource<TCosmosClient> : IIdentityAuthorizationSource
    where TCosmosClient : CosmosClient
{
    public Task<IReadOnlyDictionary<string, AccessTreeModel>> GetAccessTreesAsync(CancellationToken cancellationToken = default)
        => GetItemsAsync<AccessTreeModel>(options.AccessTreeContainerName, null, IdentityLifecycleFilter.All, cancellationToken);

    public Task<IReadOnlyDictionary<string, UserAccessTreeModel>> GetUserAccessTreesAsync(CancellationToken cancellationToken = default)
        => GetItemsAsync<UserAccessTreeModel>(options.UserAccessTreeContainerName, null, IdentityLifecycleFilter.All, cancellationToken);

    public Task<IReadOnlyDictionary<string, TeamUserModel>> GetTeamUsersAsync(CancellationToken cancellationToken = default)
        => GetItemsAsync<TeamUserModel>(options.TeamUserContainerName, null, IdentityLifecycleFilter.All, cancellationToken);

    public async Task RefreshAuthorizationAsync(CancellationToken cancellationToken = default)
    {
        await RefreshFamilyAsync<AccessTreeModel>(options.AccessTreeContainerName, null, cancellationToken);
        await RefreshFamilyAsync<UserAccessTreeModel>(options.UserAccessTreeContainerName, null, cancellationToken);
        await RefreshFamilyAsync<TeamUserModel>(options.TeamUserContainerName, null, cancellationToken);
    }
}
