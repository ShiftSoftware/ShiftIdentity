namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor;

public class ShiftIdentityDashboardBlazorOptions
{
    public string Title { get; set; } = default!;
    public string LogoPath { get; set; } = default!;
    //public bool StandAlone { get; set; }


    public string BaseAssress { get; set; } = default!;

    public ShiftIdentityDashboardRoutes DashboardRoutes { get; set; } = new ShiftIdentityDashboardRoutes();
}
