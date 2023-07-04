using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftIdentity.Core.DTOs.App;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Service;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions;

public static class ShiftEntityODataOptionsExtensions
{
    public static ShiftEntityODataOptions RegisterShiftIdentityDashboardEntitySets(this ShiftEntityODataOptions shiftEntityODataOptions)
    {
        shiftEntityODataOptions.OdataEntitySet<AppDTO>($"{Core.Constants.IdentityRoutePreifix}App");
        shiftEntityODataOptions.OdataEntitySet<AccessTreeDTO>($"{Core.Constants.IdentityRoutePreifix}AccessTree");
        shiftEntityODataOptions.OdataEntitySet<UserListDTO>($"{Core.Constants.IdentityRoutePreifix}User");
        shiftEntityODataOptions.OdataEntitySet<PublicUserListDTO>($"{Core.Constants.IdentityRoutePreifix}PublicUser");


        shiftEntityODataOptions.OdataEntitySet<DepartmentListDTO>($"{Core.Constants.IdentityRoutePreifix}Department");
        shiftEntityODataOptions.OdataEntitySet<RegionListDTO>($"{Core.Constants.IdentityRoutePreifix}Region");
        shiftEntityODataOptions.OdataEntitySet<ServiceListDTO>($"{Core.Constants.IdentityRoutePreifix}Service");
        shiftEntityODataOptions.OdataEntitySet<CompanyListDTO>($"{Core.Constants.IdentityRoutePreifix}Company");
        shiftEntityODataOptions.OdataEntitySet<CompanyBranchListDTO>($"{Core.Constants.IdentityRoutePreifix}CompanyBranch");

        return shiftEntityODataOptions;
    }
}
