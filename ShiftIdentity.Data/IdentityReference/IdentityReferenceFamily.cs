namespace ShiftSoftware.ShiftIdentity.Data.IdentityReference;

/// <summary>
/// One family of identity rows. Names what to refresh — see
/// <see cref="IIdentityReferenceSource.RefreshAsync(IdentityReferenceFamily, CancellationToken)"/>.
///
/// <para>It exists so refreshing is two methods instead of twenty, and it is deliberately not a way to
/// select a backend or to configure anything per family. There is one cache setting and it applies to
/// everything: ten knobs whose misconfiguration is silent — a stale name looks exactly like a correct one —
/// would be a liability, and these families do not differ enough to earn one each.</para>
/// </summary>
public enum IdentityReferenceFamily
{
    Countries = 0,
    Regions = 1,
    Cities = 2,
    Companies = 3,
    CompanyBranches = 4,
    Services = 5,
    Departments = 6,
    Teams = 7,
    Brands = 8,
    Users = 9,
}
