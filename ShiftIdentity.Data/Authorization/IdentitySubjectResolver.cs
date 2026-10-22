using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
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
/// Decides, for each candidate user, whether that user could sign in now, and prepares the claims and grants that
/// sign-in would give them. It reads the replicated Identity documents through <see cref="IIdentityReferenceSource"/>,
/// never Identity's SQL database or API. This is the first half of "could this user open this record". The second half
/// is the repository's <c>CanAccessAs</c>, and the caller checks the screen permission with the subject's TypeAuth.
/// <para>
/// <b>One outcome per user, one user at a time.</b> Each user is Eligible, NotEligible (with a reason) or Unresolved
/// (with a reason and a detail). NotEligible matches sign-in's own refusals and is not an error. Unresolved means the
/// documents do not say for sure; never deliver to such a user. One user's problem never affects another user, with one
/// exception: when a team document has no member list, no user's team claims are known for sure, so every user who
/// would otherwise be prepared is Unresolved.
/// </para>
/// <para>
/// <b>Missing documents.</b> Before it reports a document as missing, it reads that one document again from the store,
/// once per scope. It retries nothing else. The caller records the unresolved users and retries them in a later scope,
/// while the event is still valid.
/// </para>
/// <para>
/// <b>Scoped.</b> One HTTP request, timer run or function invocation is one scope. Within a scope, each user is
/// prepared the first time a call needs them, only once even when calls run at the same time, and every later call
/// gets the same outcome. Nothing outlives the scope, so a change is seen from the next scope; a long job should run in
/// batches.
/// </para>
/// <para>
/// <b>Claims and grants</b> are those that token issuance gives (<c>AdmissionRules.Issue</c>): the scope IDs and the
/// city from the user's branch, the company type, the country when the user has one, one team claim per membership,
/// and the user's own access tree followed by the assigned named trees, in the order of the user document's list. That
/// list is in the order of the assignments, the order sign-in adds them, and the merge depends on it: for a text action
/// without a comparer the last tree wins. Soft-deleted named trees and teams still apply, as they do at sign-in. The action trees are the host's registered ones
/// (<see cref="ITypeAuthService.GetRegisteredActionTrees"/>), the same set the host's own requests use.
/// </para>
/// </summary>
public sealed class IdentitySubjectResolver
{
    private readonly IIdentityReferenceSource references;
    private readonly ITypeAuthService typeAuth;
    private readonly IHashIdService hashIds;

    // Each user's outcome in this scope, and the documents read again from the store in this scope.
    private readonly ConcurrentDictionary<long, Lazy<Task<Outcome>>> outcomes = new();
    private readonly ConcurrentDictionary<(IdentityReferenceFamily Family, long Id), Lazy<Task<bool>>> refreshes = new();

    // The memberships are built once per team roster, not once per user.
    private readonly object membershipsGate = new();
    private (IReadOnlyDictionary<string, TeamModel> Roster, Memberships Value)? memberships;

    private Type[]? actionTrees;

    public IdentitySubjectResolver(IIdentityReferenceSource references, ITypeAuthService typeAuth, IHashIdService hashIds)
    {
        this.references = references ?? throw new ArgumentNullException(nameof(references));
        this.typeAuth = typeAuth ?? throw new ArgumentNullException(nameof(typeAuth));
        this.hashIds = hashIds ?? throw new ArgumentNullException(nameof(hashIds));
    }

    /// <summary>
    /// The outcome for each of <paramref name="userIds"/>. A full-population check is the same call with every user ID.
    /// A failed read of the store throws, and nothing is kept for the users it affected.
    /// </summary>
    public async Task<IdentitySubjectResolution> ResolveAsync(IEnumerable<long> userIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        var ids = userIds.Distinct().Order().ToArray();
        var prepared = await Task.WhenAll(ids.Select(id => Once(outcomes, id, key => PrepareAsync(key, cancellationToken))));

        var eligible = new List<IdentityAccessSubject>();
        var notEligible = new Dictionary<long, IdentitySubjectNotEligibleReason>();
        var unresolved = new Dictionary<long, IdentitySubjectUnresolved>();

        for (var i = 0; i < ids.Length; i++)
        {
            switch (prepared[i])
            {
                case EligibleOutcome outcome:
                    eligible.Add(outcome.Subject);
                    break;
                case NotEligibleOutcome outcome:
                    notEligible[ids[i]] = outcome.Reason;
                    break;
                case UnresolvedOutcome outcome:
                    unresolved[ids[i]] = outcome.Unresolved;
                    break;
            }
        }

        return new(eligible, notEligible, unresolved);
    }

    private async Task<Outcome> PrepareAsync(long userId, CancellationToken cancellationToken)
    {
        var user = await references.ResolveUserAsync(userId, cancellationToken);

        if (user is null)
        {
            await RefreshOnceAsync(IdentityReferenceFamily.Users, userId, cancellationToken);
            user = await references.ResolveUserAsync(userId, cancellationToken);
        }

        if (user is null)
            return Unresolved(IdentitySubjectUnresolvedReason.UserMissing, $"User {userId} has no document.");

        // The refusals of sign-in come first: they do not depend on anything else.
        if (user.IsDeleted)
            return NotEligible(IdentitySubjectNotEligibleReason.Deleted);

        if (user.IsActive is null)
            return Unresolved(IdentitySubjectUnresolvedReason.UserOutdated, $"User {userId}'s document has no IsActive.");

        if (user.IsActive == false)
            return NotEligible(IdentitySubjectNotEligibleReason.Inactive);

        if (user.CompanyID is not { } companyId || user.CompanyBranchID is not { } branchId || user.RegionID is not { } regionId)
            return NotEligible(IdentitySubjectNotEligibleReason.NoSignInScope);

        var branch = await FromRosterAsync(IdentityReferenceFamily.CompanyBranches, branchId,
            token => references.GetCompanyBranchesAsync(IdentityLifecycleFilter.All, token), cancellationToken);

        if (branch is null)
            return Unresolved(IdentitySubjectUnresolvedReason.ReferenceMissing, $"Company branch {branchId} has no document.");

        if (branch.CityID is not { } cityId)
            return NotEligible(IdentitySubjectNotEligibleReason.NoSignInScope);

        if (user.AccessTreeIDs is not { } accessTreeIds)
            return Unresolved(IdentitySubjectUnresolvedReason.UserOutdated, $"User {userId}'s document has no AccessTreeIDs.");

        if (user.Username is null || user.FullName is null)
            return Unresolved(IdentitySubjectUnresolvedReason.UserOutdated, $"User {userId}'s document has no username or full name.");

        var company = await FromRosterAsync(IdentityReferenceFamily.Companies, companyId,
            token => references.GetCompaniesAsync(IdentityLifecycleFilter.All, token), cancellationToken);

        if (company is null)
            return Unresolved(IdentitySubjectUnresolvedReason.ReferenceMissing, $"Company {companyId} has no document.");

        var teams = await MembershipsAsync(cancellationToken);

        if (teams.Unknown.Count > 0)
            return Unresolved(IdentitySubjectUnresolvedReason.TeamMembersUnknown,
                $"Team {string.Join(", ", teams.Unknown)} has no member list, so no user's teams are known for sure.");

        var namedTrees = new List<(long Id, string Tree)>();

        foreach (var accessTreeId in accessTreeIds)
        {
            var tree = await FromRosterAsync(IdentityReferenceFamily.AccessTrees, accessTreeId,
                token => references.GetAccessTreesAsync(IdentityLifecycleFilter.All, token), cancellationToken);

            if (tree is null)
                return Unresolved(IdentitySubjectUnresolvedReason.ReferenceMissing, $"Access tree {accessTreeId} has no document.");

            if (string.IsNullOrWhiteSpace(tree.Tree))
                return Unresolved(IdentitySubjectUnresolvedReason.AccessTreeInvalid, $"Access tree {accessTreeId} is empty.");

            namedTrees.Add((accessTreeId, tree.Tree));
        }

        // The claims token issuance gives, with the same hashed encoding.
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, hashIds.Encode<UserDTO>(userId)),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.GivenName, user.FullName),
            new(DataClaims.CompanyIdClaim, hashIds.Encode<CompanyDTO>(companyId)),
            new(DataClaims.CompanyBranchIdClaim, hashIds.Encode<CompanyBranchDTO>(branchId)),
            new(DataClaims.RegionIdClaim, hashIds.Encode<RegionDTO>(regionId)),
            new(DataClaims.CompanyTypeClaim, company.CompanyType.ToString()),
            new(DataClaims.CityIdClaim, hashIds.Encode<CityDTO>(cityId)),
        };

        if (user.CountryID is { } countryId)
            claims.Add(new(DataClaims.CountryIdClaim, hashIds.Encode<CountryDTO>(countryId)));
        if (user.Email is not null)
            claims.Add(new(ClaimTypes.Email, user.Email));
        if (user.Phone is not null)
            claims.Add(new(ClaimTypes.MobilePhone, user.Phone));

        // A membership grants no action itself; it is a claim the data-level rules read.
        foreach (var teamId in teams.TeamsByUser[userId])
            claims.Add(new(DataClaims.TeamIdsClaim, hashIds.Encode<TeamDTO>(teamId)));

        // The user's own tree, then the named trees: the same trees and order as the session's access tree claims. An
        // empty own tree means the user has no direct grants, as at sign-in.
        var registered = ActionTrees();
        var builder = new TypeAuthContextBuilder();

        foreach (var actionTree in registered)
            builder.AddActionTree(actionTree);

        if (!string.IsNullOrWhiteSpace(user.AccessTree))
            builder.AddAccessTree(user.AccessTree);

        foreach (var (_, tree) in namedTrees)
            builder.AddAccessTree(tree);

        TypeAuthContext grants;

        try
        {
            grants = builder.Build();
        }
        catch (Exception)
        {
            return Unresolved(IdentitySubjectUnresolvedReason.AccessTreeInvalid, UnreadableTree(user, namedTrees, registered));
        }

        return new EligibleOutcome(new IdentityAccessSubject(userId, grants, claims));
    }

    // A document from its family's roster. A missing one is read again from the store, once, before it counts as missing.
    private async Task<TModel?> FromRosterAsync<TModel>(IdentityReferenceFamily family, long id,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, TModel>>> roster, CancellationToken cancellationToken)
        where TModel : class
    {
        var key = id.ToString(CultureInfo.InvariantCulture);

        if ((await roster(cancellationToken)).TryGetValue(key, out var found))
            return found;

        await RefreshOnceAsync(family, id, cancellationToken);

        return (await roster(cancellationToken)).TryGetValue(key, out found) ? found : null;
    }

    private Task<bool> RefreshOnceAsync(IdentityReferenceFamily family, long id, CancellationToken cancellationToken)
        => Once(refreshes, (family, id), async ((IdentityReferenceFamily Family, long Id) key) =>
        {
            await references.RefreshAsync(key.Family, key.Id, cancellationToken);
            return true;
        });

    // Each user's teams, from the member lists on the team documents. A team whose document has no member list is read
    // again from the store, once, and stays in Unknown when it still has none.
    private async Task<Memberships> MembershipsAsync(CancellationToken cancellationToken)
    {
        var roster = await references.GetTeamsAsync(IdentityLifecycleFilter.All, cancellationToken);
        var outdated = roster.Values.Where(team => team.UserIDs is null).Select(team => team.id).ToList();

        if (outdated.Count > 0)
        {
            foreach (var teamId in outdated)
                await RefreshOnceAsync(IdentityReferenceFamily.Teams, long.Parse(teamId, CultureInfo.InvariantCulture), cancellationToken);

            roster = await references.GetTeamsAsync(IdentityLifecycleFilter.All, cancellationToken);
        }

        lock (membershipsGate)
        {
            if (memberships is { } cached && ReferenceEquals(cached.Roster, roster))
                return cached.Value;

            var value = new Memberships(
                roster.Values
                    .Where(team => team.UserIDs is not null)
                    .SelectMany(team => team.UserIDs!.Select(member => (UserId: member, TeamId: long.Parse(team.id, CultureInfo.InvariantCulture))))
                    .OrderBy(membership => membership.TeamId)
                    .ToLookup(membership => membership.UserId, membership => membership.TeamId),
                roster.Values.Where(team => team.UserIDs is null).Select(team => team.id).Order(StringComparer.Ordinal).ToList());

            memberships = (roster, value);
            return value;
        }
    }

    private Type[] ActionTrees()
    {
        var registered = actionTrees ??= typeAuth.GetRegisteredActionTrees();

        if (registered.Length == 0)
            throw new InvalidOperationException(
                "The host registers no TypeAuth action trees, so no user's grants can be prepared. Register them with AddTypeAuth.");

        return registered;
    }

    // Names the tree that does not parse: the user's own tree, or the first named tree that fails on its own.
    private static string UnreadableTree(UserModel user, IReadOnlyList<(long Id, string Tree)> namedTrees, Type[] registered)
    {
        if (!string.IsNullOrWhiteSpace(user.AccessTree) && !Parses(user.AccessTree, registered))
            return $"User {user.id}'s own access tree does not parse.";

        foreach (var (id, tree) in namedTrees)
            if (!Parses(tree, registered))
                return $"Access tree {id} does not parse.";

        return $"User {user.id}'s access trees cannot be merged.";
    }

    private static bool Parses(string tree, Type[] registered)
    {
        try
        {
            var builder = new TypeAuthContextBuilder();

            foreach (var actionTree in registered)
                builder.AddActionTree(actionTree);

            builder.AddAccessTree(tree).Build();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Starts the work for a key once, and gives every caller in the scope the same task. Work that fails or is
    // cancelled is not kept, so the next caller starts it again.
    private static Task<TValue> Once<TKey, TValue>(ConcurrentDictionary<TKey, Lazy<Task<TValue>>> map, TKey key, Func<TKey, Task<TValue>> start)
        where TKey : notnull
    {
        var entry = map.GetOrAdd(key, newKey =>
        {
            Lazy<Task<TValue>>? created = null;
            created = new(() =>
            {
                var task = start(newKey);
                task.ContinueWith(_ => map.TryRemove(KeyValuePair.Create(newKey, created!)), CancellationToken.None,
                    TaskContinuationOptions.NotOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                return task;
            });
            return created;
        });

        return entry.Value;
    }

    private static Outcome NotEligible(IdentitySubjectNotEligibleReason reason) => new NotEligibleOutcome(reason);

    private static Outcome Unresolved(IdentitySubjectUnresolvedReason reason, string detail)
        => new UnresolvedOutcome(new IdentitySubjectUnresolved(reason, detail));

    private sealed class Memberships(ILookup<long, long> teamsByUser, IReadOnlyList<string> unknown)
    {
        public ILookup<long, long> TeamsByUser { get; } = teamsByUser;
        public IReadOnlyList<string> Unknown { get; } = unknown;
    }

    private abstract record Outcome;
    private sealed record EligibleOutcome(IdentityAccessSubject Subject) : Outcome;
    private sealed record NotEligibleOutcome(IdentitySubjectNotEligibleReason Reason) : Outcome;
    private sealed record UnresolvedOutcome(IdentitySubjectUnresolved Unresolved) : Outcome;
}
