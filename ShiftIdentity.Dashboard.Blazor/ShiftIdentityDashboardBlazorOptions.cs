using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor;

public class ShiftIdentityDashboardBlazorOptions
{
    internal Dictionary<string, CustomFieldBase> CompanyBranchCustomFields = new();
    internal Dictionary<string, CustomFieldBase> CompanyCustomFields = new();
    internal List<string> CompanyBranchPhoneTags = new();
    internal List<string> CompanyBranchEmailTags = new();
    internal List<string> TeamTags = new();

    public string Title { get; set; } = default!;
    public string LogoPath { get; set; } = default!;
    public ShiftIdentityHostingTypes ShiftIdentityHostingType { get; set; }


    public string BaseAddress { get; set; } = default!;

    public ShiftIdentityDashboardRoutes DashboardRoutes { get; set; } = new ShiftIdentityDashboardRoutes();

    public DayOfWeek WeekStart { get; set; } = DayOfWeek.Saturday;

    public Func<Task>? DynamicTypeAuthActionExpander { get; set; }

    /// <summary>
    /// True when the identity API this dashboard talks to registers the staged authority. The self-service
    /// security screens (password change, authenticator set-up and replacement) then use the staged flows under
    /// <c>api/identity/v2</c> instead of the deployed <c>UserManager</c> routes, which that authority serves only
    /// for the forced steps of a login. The identity host's own client sets this at its cutover, together with the
    /// API's registration; consumers on the old package are unaffected. Temporary: Phase 6 makes the staged
    /// authority the only one and removes this switch with the legacy branches it selects between.
    /// </summary>
    public bool StagedAuthority { get; set; }

    public ShiftIdentityDashboardBlazorOptions AddCompanyBranchPhoneTag(string tag)
    {
        this.CompanyBranchPhoneTags.Add(tag);

        return this;
    }

    public ShiftIdentityDashboardBlazorOptions AddCompanyBranchEmailTag(string tag)
    {
        this.CompanyBranchEmailTags.Add(tag);

        return this;
    }

    public ShiftIdentityDashboardBlazorOptions AddTeamTag(string tag)
    {
        this.TeamTags.Add(tag);

        return this;
    }

    public ShiftIdentityDashboardBlazorOptions AddCompanyBranchCustomField(string fieldName, string displayName,
        bool isPassword = false, bool isEncrypted = false)
    {
        this.CompanyBranchCustomFields.Add(fieldName, new CustomFieldBase(displayName ?? fieldName, isPassword, isEncrypted));
        return this;
    }

    public ShiftIdentityDashboardBlazorOptions AddCompanyBranchCustomField(string fieldName,
        bool isPassword = false, bool isEncrypted = false)
    {
        this.CompanyBranchCustomFields.Add(fieldName, new CustomFieldBase(fieldName, isPassword, isEncrypted));
        return this;
    }

    public ShiftIdentityDashboardBlazorOptions AddCompanyCustomField(string fieldName, string displayName,
        bool isPassword = false, bool isEncrypted = false)
    {
        this.CompanyCustomFields.Add(fieldName, new CustomFieldBase(displayName ?? fieldName, isPassword, isEncrypted));
        return this;
    }

    public ShiftIdentityDashboardBlazorOptions AddCompanyCustomField(string fieldName,
        bool isPassword = false, bool isEncrypted = false)
    {
        this.CompanyCustomFields.Add(fieldName, new CustomFieldBase(fieldName, isPassword, isEncrypted));
        return this;
    }
}
