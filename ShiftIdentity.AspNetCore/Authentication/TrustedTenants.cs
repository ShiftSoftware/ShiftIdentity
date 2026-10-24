using ShiftSoftware.ShiftIdentity.Core.Models;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>A Microsoft tenant the host trusts: its ID, the email domains held to it, and whether its sign-ins skip Shift MFA.</summary>
internal sealed record TrustedTenant(string ID, IReadOnlyList<string> Domains, bool SkipShiftMfa);

/// <summary>
/// The host's trusted Microsoft tenants. An account whose email is on a tenant's domain signs in only with Microsoft
/// through that tenant; a sign-in from a tenant with <see cref="TrustedTenant.SkipShiftMfa"/> is never asked for the
/// Shift authenticator. Built once from the settings and validated at startup.
/// </summary>
internal sealed class TrustedTenants
{
    internal static readonly TrustedTenants None = new([]);

    private readonly Dictionary<string, TrustedTenant> byDomain = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrustedTenant> byID = new(StringComparer.OrdinalIgnoreCase);

    private TrustedTenants(IEnumerable<TrustedTenant> tenants)
    {
        foreach (var tenant in tenants)
        {
            byID.Add(tenant.ID, tenant);
            foreach (var domain in tenant.Domains) byDomain.Add(domain, tenant);
        }
    }

    public bool Any => byID.Count > 0;

    /// <summary>Every domain held to a tenant, for the login screen.</summary>
    public IReadOnlyList<string> Domains => byDomain.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>The tenant an email's domain is held to, or null. The match is exact and in any case.</summary>
    public TrustedTenant? ForEmail(string? email)
    {
        if (byDomain.Count == 0 || email is null) return null;
        var trimmed = email.Trim();
        var at = trimmed.LastIndexOf('@');
        return at > 0 && byDomain.TryGetValue(trimmed[(at + 1)..], out var tenant) ? tenant : null;
    }

    /// <summary>The trusted tenant with this ID, or null.</summary>
    public TrustedTenant? Find(string? tenantID) =>
        tenantID is not null && byID.TryGetValue(tenantID, out var tenant) ? tenant : null;

    /// <summary>
    /// Validates the settings. A malformed tenant ID, the personal-account tenant, a malformed or repeated domain, or
    /// trusted tenants while Microsoft sign-in is off fail at startup, so a mistake never lets a password back in.
    /// </summary>
    internal static TrustedTenants Create(MicrosoftSignInSettings? microsoft, Func<string, string, Exception> invalid)
    {
        if (microsoft?.TrustedTenants is not { Count: > 0 } configured) return None;
        if (!microsoft.Enabled)
            throw invalid("Authority.Microsoft.TrustedTenants", "needs Microsoft sign-in: set Authority.Microsoft.Enabled, or remove the trusted tenants. Their accounts would have no way to sign in.");
        var tenants = new List<TrustedTenant>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var settings in configured)
        {
            if (settings is null || !Guid.TryParse(settings.TenantId?.Trim(), out var id))
                throw invalid("Authority.Microsoft.TrustedTenants", "must give each tenant its TenantId: the Directory (tenant) ID GUID from its Entra ID overview.");
            var tenantID = id.ToString("D");
            if (tenantID == MicrosoftSignIn.ConsumerTenant)
                throw invalid("Authority.Microsoft.TrustedTenants", "cannot trust the personal Microsoft account tenant: anyone can make a personal account on any address.");
            if (!ids.Add(tenantID)) throw invalid("Authority.Microsoft.TrustedTenants", $"names the tenant {tenantID} twice.");
            var own = new List<string>();
            foreach (var value in settings.Domains ?? [])
            {
                var domain = value?.Trim().TrimStart('@') ?? "";
                if (!IsDomain(domain))
                    throw invalid("Authority.Microsoft.TrustedTenants", $"has the domain '{value}' on the tenant {tenantID}. A domain is the part of an email after @, such as example.com.");
                if (!domains.Add(domain)) throw invalid("Authority.Microsoft.TrustedTenants", $"names the domain {domain} more than once. A domain belongs to one tenant.");
                own.Add(domain.ToLowerInvariant());
            }
            if (own.Count == 0)
                throw invalid("Authority.Microsoft.TrustedTenants", $"must give the tenant {tenantID} at least one email domain.");
            tenants.Add(new(tenantID, own.AsReadOnly(), settings.SkipShiftMfa));
        }
        return new(tenants);
    }

    private static bool IsDomain(string value) => value.Length is > 2 and <= 253 && value.Contains('.') &&
        !value.StartsWith('.') && !value.EndsWith('.') && !value.Contains("..") &&
        value.All(x => char.IsAsciiLetterOrDigit(x) || x is '.' or '-');
}
