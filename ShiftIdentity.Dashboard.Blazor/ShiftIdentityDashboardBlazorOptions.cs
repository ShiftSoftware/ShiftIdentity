using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor;

public class ShiftIdentityDashboardBlazorOptions
{
    public string Title { get; set; } = default!;
    public string LogoPath { get; set; } = default!;
    public ShiftIdentityHostingTypes ShiftIdentityHostingType { get; set; }


    public string BaseAssress { get; set; } = default!;

    public ShiftIdentityDashboardRoutes DashboardRoutes { get; set; } = new ShiftIdentityDashboardRoutes();
}
