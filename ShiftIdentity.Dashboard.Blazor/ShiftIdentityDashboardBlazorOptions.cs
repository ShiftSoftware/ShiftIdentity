using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor;

public class ShiftIdentityDashboardBlazorOptions
{
    internal Dictionary<string, CustomFieldBase> CompanyBranchCustomFields = new();

    public string Title { get; set; } = default!;
    public string LogoPath { get; set; } = default!;
    public ShiftIdentityHostingTypes ShiftIdentityHostingType { get; set; }


    public string BaseAssress { get; set; } = default!;

    public ShiftIdentityDashboardRoutes DashboardRoutes { get; set; } = new ShiftIdentityDashboardRoutes();

    public Func<Task>? DynamicTypeAuthActionExpander { get; set; }

    public ShiftIdentityDashboardBlazorOptions AddCompanyBranchCustomField(string fieldName,
        bool isPassword = false, bool isEncrypted = false)
    {
        this.CompanyBranchCustomFields.Add(fieldName, new CustomFieldBase(isPassword, isEncrypted));
        return this;
    }
}
