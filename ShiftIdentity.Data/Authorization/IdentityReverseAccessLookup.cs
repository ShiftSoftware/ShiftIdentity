using System.Globalization;
using System.Security.Claims;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Core.DataLevelAccess;
using ShiftSoftware.ShiftIdentity.Core.DTOs.City;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Country;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Team;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Data.IdentityReference;
using ShiftSoftware.TypeAuth.Core;
using DataClaims = ShiftSoftware.ShiftEntity.Core.Constants;

namespace ShiftSoftware.ShiftIdentity.Data.Authorization;

/// <summary>
/// Builds candidate contexts from existing replicated users, references, grants and memberships.
/// Uses the source's configured caching; it does not call Identity SQL or an Identity API.
/// Reads are not an atomic cross-container snapshot. Replication lag and cache expiry remain those of the host.
/// </summary>
public sealed class IdentityReverseAccessLookup(
    IIdentityReferenceSource references, IIdentityAuthorizationSource authorization, IHashIdService hashIds)
{
    /// <summary>
    /// Returns deduplicated raw user ids passing system and record access. The operation is explicit.
    /// A missing effective policy is an error; legacy consumers can use GetSubjectsAsync with their existing evaluator.
    /// </summary>
    public async Task<IReadOnlyList<string>> FindUsersAsync<TEntity>(TEntity entity, Access operation,
        DataLevelAccessPolicy<TEntity> policy, Func<IdentityAccessSubject, Access, bool> hasSystemAccess,
        IEnumerable<Type> actionTrees, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(hasSystemAccess);
        var subjects = await GetSubjectsAsync(actionTrees, cancellationToken);
        return subjects.Where(subject => subject.CanAccess(entity, operation, policy, hasSystemAccess))
            .Select(subject => subject.UserId).Distinct().ToArray();
    }

    /// <summary>
    /// Builds private contexts for active, non-deleted users. All tree sources are merged before evaluation.
    /// Missing required documents or old users without IsActive throw, so a partial lookup is not mistaken for
    /// a complete audience. A complete backfill of all families is required before enabling a consumer.
    /// For legacy policies construct IdentityClaimProvider(subject, hashIds) and DefaultDataLevelAccess
    /// from this subject, then use the repository's actual options alongside its system-access check.
    /// </summary>
    public async Task<IReadOnlyList<IdentityAccessSubject>> GetSubjectsAsync(IEnumerable<Type> actionTrees,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actionTrees);
        var registeredTrees = actionTrees.ToArray();
        if (registeredTrees.Length == 0)
            throw new ArgumentException("Register the system and data-level action trees used by the consumer.", nameof(actionTrees));

        var users = await references.GetUsersAsync(IdentityLifecycleFilter.All, cancellationToken);
        var trees = await authorization.GetAccessTreesAsync(cancellationToken);
        var assignments = (await authorization.GetUserAccessTreesAsync(cancellationToken)).Values.ToLookup(x => x.UserID);
        var memberships = (await authorization.GetTeamUsersAsync(cancellationToken)).Values.ToLookup(x => x.UserID);
        var companies = await references.GetCompaniesAsync(IdentityLifecycleFilter.All, cancellationToken);
        var branches = await references.GetCompanyBranchesAsync(IdentityLifecycleFilter.All, cancellationToken);
        var subjects = new List<IdentityAccessSubject>();

        foreach (var user in users.Values.OrderBy(user => user.id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (user.IsDeleted || user.IsActive == false)
                continue;
            if (user.IsActive is null)
                throw new InvalidOperationException($"User '{user.id}' has no replicated authorization state. Backfill Users first.");

            var userId = long.Parse(user.id, CultureInfo.InvariantCulture);
            var companyId = user.CompanyID ?? throw Missing(user.id, "CompanyID");
            var branchId = user.CompanyBranchID ?? throw Missing(user.id, "CompanyBranchID");
            var regionId = user.RegionID ?? throw Missing(user.id, "RegionID");
            if (!companies.TryGetValue(companyId.ToString(CultureInfo.InvariantCulture), out var company))
                throw Missing(user.id, "Company");
            if (!branches.TryGetValue(branchId.ToString(CultureInfo.InvariantCulture), out var branch))
                throw Missing(user.id, "CompanyBranch");

            // Match Identity token claims. City comes from the branch document, so branch edits need
            // only the already existing branch replication. Other scope ids are the user's own columns.
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, hashIds.Encode<UserDTO>(userId)),
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.GivenName, user.FullName),
                new(DataClaims.CompanyIdClaim, hashIds.Encode<CompanyDTO>(companyId)),
                new(DataClaims.CompanyBranchIdClaim, hashIds.Encode<CompanyBranchDTO>(branchId)),
                new(DataClaims.RegionIdClaim, hashIds.Encode<RegionDTO>(regionId)),
                new(DataClaims.CompanyTypeClaim, company.CompanyType.ToString()),
                new(DataClaims.CityIdClaim, hashIds.Encode<CityDTO>(branch.CityID ?? throw Missing(user.id, "CityID"))),
            };
            if (user.CountryID is { } countryId)
                claims.Add(new(DataClaims.CountryIdClaim, hashIds.Encode<CountryDTO>(countryId)));
            if (user.Email is not null) claims.Add(new(ClaimTypes.Email, user.Email));
            if (user.Phone is not null) claims.Add(new(ClaimTypes.MobilePhone, user.Phone));

            // Identity token issuance uses the assignment and membership joins without testing the
            // lifecycle of the named tree or team. Preserve that rule; membership grants no action itself.
            foreach (var membership in memberships[userId])
                claims.Add(new(DataClaims.TeamIdsClaim, hashIds.Encode<TeamDTO>(membership.TeamID)));

            var builder = new TypeAuthContextBuilder();
            foreach (var tree in registeredTrees) builder.AddActionTree(tree);
            if (!string.IsNullOrWhiteSpace(user.AccessTree)) builder.AddAccessTree(user.AccessTree);
            foreach (var assignment in assignments[userId])
            {
                if (!trees.TryGetValue(assignment.AccessTreeID.ToString(CultureInfo.InvariantCulture), out var tree))
                    throw Missing(user.id, $"AccessTree {assignment.AccessTreeID}");
                if (string.IsNullOrWhiteSpace(tree.Tree))
                    throw Missing(user.id, $"AccessTree {assignment.AccessTreeID} content");
                builder.AddAccessTree(tree.Tree);
            }
            subjects.Add(new(user.id, builder.Build(), claims, hashIds));
        }

        return subjects;
    }

    private static InvalidOperationException Missing(string userId, string field)
        => new($"Replicated user '{userId}' is missing '{field}'. The authorization lookup is incomplete.");
}
