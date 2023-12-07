using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor;

public class ShiftIdentityDashboardBlazorOptions
{
    internal Dictionary<string, CustomFieldBase> CompanyBranchCustomFields = new();
    internal Dictionary<string, CustomFieldBase> CompanyCustomFields = new();

    public string Title { get; set; } = default!;
    public string LogoPath { get; set; } = default!;
    public ShiftIdentityHostingTypes ShiftIdentityHostingType { get; set; }


    public string BaseAddress { get; set; } = default!;

    public ShiftIdentityDashboardRoutes DashboardRoutes { get; set; } = new ShiftIdentityDashboardRoutes();

    public Func<Task>? DynamicTypeAuthActionExpander { get; set; }

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
