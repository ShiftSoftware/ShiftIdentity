using System.Security.Claims;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Core.DataLevelAccess;
using ShiftSoftware.ShiftEntity.Model.Flags;
using ShiftSoftware.ShiftEntity.Web.Services;
using ShiftSoftware.ShiftIdentity.Data.Authorization;
using ShiftSoftware.TypeAuth.Core;
using ShiftSoftware.TypeAuth.Core.Actions;

namespace ShiftIdentity.ReverseAccess.Benchmarks;

// SSC-shaped records, not business records loaded from a ticket database.
internal sealed record ScopeRow : IEntityHasRegion<ScopeRow>, IEntityHasCompany<ScopeRow>,
    IEntityHasCompanyBranch<ScopeRow>, IEntityHasBrand<ScopeRow>, IEntityHasCity<ScopeRow>,
    IEntityHasTeam<ScopeRow>, IEntityHasCountry<ScopeRow>
{
    public long? RegionID { get; set; }
    public long? CompanyID { get; set; }
    public long? CompanyBranchID { get; set; }
    public long? BrandID { get; set; }
    public long? CityID { get; set; }
    public long? TeamID { get; set; }
    public long? CountryID { get; set; }
    public long? OwnerID { get; set; }
    public long? AlternateCompanyID { get; set; }

    public static DataLevelAccessBuilder<ScopeRow> Builder(bool allDimensions = false)
        => new DataLevelAccessBuilder<ScopeRow>().AddStandardDimensions(new()
        {
            DisableDefaultCountryFilter = !allDimensions,
            DisableDefaultCityFilter = !allDimensions,
            DisableDefaultTeamFilter = !allDimensions,
        });
}

// This is deliberately harness-local. The manifest comes from the effective builder, not from
// guessed permission JSON paths. It must include every policy/action/self-vector the consumer needs.
internal sealed class ScopeProjection : IAccessibleItemsSource
{
    private readonly Dictionary<DynamicAction, List<(string[] Self, AccessibleItemsByAccess Bundle)>> values = [];
    public int BundleCount => values.Values.Sum(x => x.Count);

    public ScopeProjection(ITypeAuthService engine, ClaimsPrincipal principal,
        IEnumerable<DataLevelDimension<ScopeRow>> dimensions)
    {
        foreach (var dimension in dimensions)
        {
            if (dimension.ValueSource is not TypeAuthValueSource source) continue;
            var self = dimension.SelfClaimType is null ? [] : principal.FindAll(dimension.SelfClaimType)
                .Select(c => c.Value).Take(dimension.UsesAllSelfClaims ? int.MaxValue : 1).ToArray();
            if (!values.TryGetValue(source.Action, out var variants)) values[source.Action] = variants = [];
            if (variants.Any(x => x.Self.SequenceEqual(self))) continue;
            var bundle = engine.GetAccessibleItemsByAccess(source.Action, self);
            static AccessibleItemsResult Copy(AccessibleItemsResult value) => new(value.WildCard, [..value.AccessibleIds]);
            variants.Add((self, new(Copy(bundle.Read), Copy(bundle.Write), Copy(bundle.Delete), Copy(bundle.Maximum))));
        }
    }

    public AccessibleItemsByAccess GetByAccess(DynamicAction action, params string[]? selfIds)
    {
        // All policy calls pass a non-null array. Reject other or undeclared requests rather than
        // silently treating null as empty or retaining an engine as a fallback.
        if (selfIds is not null && values.TryGetValue(action, out var variants))
            foreach (var variant in variants)
                if (variant.Self.SequenceEqual(selfIds)) return variant.Bundle;
        throw new InvalidOperationException("The projection manifest does not cover this action/self vector.");
    }
}

internal sealed class PreparedSubject
{
    internal static readonly Access[] Operations = [Access.Read, Access.Write, Access.Delete, Access.Maximum];
    private readonly IdentityAccessSubject? original;
    private readonly DataLevelAccessContext? compact;
    private readonly Func<IdentityAccessSubject, Access, bool>? gate;
    private readonly bool[]? capturedGates;
    public string Id { get; }
    public int Bundles { get; }

    private sealed class PrincipalProvider(ClaimsPrincipal principal) : ICurrentUserProvider
    { public ClaimsPrincipal GetUser() => principal; }

    public PreparedSubject(IdentityAccessSubject subject, ActionBase systemAction, bool managedEntitlement,
        IHashIdService hashIds, IEnumerable<DataLevelDimension<ScopeRow>> dimensions, bool project)
    {
        Id = subject.UserId;
        if (!project)
        {
            original = subject;
            gate = managedEntitlement ? (_, _) => true : (s, op) => s.TypeAuth.Can(systemAction, op);
            return;
        }
        var manifest = dimensions.ToArray();
        var neededClaims = manifest.Select(d => d.SelfClaimType)
            .Concat(manifest.Select(d => (d.ValueSource as OwnerClaimValueSource)?.ClaimType))
            .Where(c => c is not null).ToHashSet();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(subject.GetUser().Claims
            .Where(c => neededClaims.Contains(c.Type)).Select(c => new Claim(c.Type, c.Value)), "ScopeSnapshot"));
        var source = new ScopeProjection(subject.TypeAuth, principal, manifest);
        Bundles = source.BundleCount;
        compact = new(source, new PrincipalProvider(principal), hashIds);
        capturedGates = Operations.Select(op => managedEntitlement || subject.TypeAuth.Can(systemAction, op)).ToArray();
    }

    public bool Check(ScopeRow row, Access operation, DataLevelAccessPolicy<ScopeRow> policy)
        => original is not null ? original.CanAccess(row, operation, policy, gate!)
            : capturedGates![Array.IndexOf(Operations, operation)] && policy.Authorize(row, operation, compact!);
}
