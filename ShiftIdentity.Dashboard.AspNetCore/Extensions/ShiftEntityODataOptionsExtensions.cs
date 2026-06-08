using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Brand;
using ShiftSoftware.ShiftIdentity.Core.DTOs.City;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Service;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Team;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extensions;

public static class ShiftEntityODataOptionsExtensions
{
    //public static ShiftEntityODataOptions RegisterShiftIdentityDashboardEntitySets(this ShiftEntityODataOptions shiftEntityODataOptions)
    //{
    //    shiftEntityODataOptions.OdataEntitySet<AppDTO>($"{Core.Constants.IdentityRoutePrefix}App");
    //    shiftEntityODataOptions.OdataEntitySet<AccessTreeDTO>($"{Core.Constants.IdentityRoutePrefix}AccessTree");
    //    shiftEntityODataOptions.OdataEntitySet<UserListDTO>($"{Core.Constants.IdentityRoutePrefix}User");
    //    shiftEntityODataOptions.OdataEntitySet<TeamListDTO>($"{Core.Constants.IdentityRoutePrefix}Team");
    //    shiftEntityODataOptions.OdataEntitySet<PublicUserListDTO>($"{Core.Constants.IdentityRoutePrefix}PublicUser");


    //    shiftEntityODataOptions.OdataEntitySet<DepartmentListDTO>($"{Core.Constants.IdentityRoutePrefix}Department");
    //    shiftEntityODataOptions.OdataEntitySet<RegionListDTO>($"{Core.Constants.IdentityRoutePrefix}Region");
    //    shiftEntityODataOptions.OdataEntitySet<CityListDTO>($"{Core.Constants.IdentityRoutePrefix}City");
    //    shiftEntityODataOptions.OdataEntitySet<ServiceListDTO>($"{Core.Constants.IdentityRoutePrefix}Service");
    //    shiftEntityODataOptions.OdataEntitySet<BrandListDTO>($"{Core.Constants.IdentityRoutePrefix}Brand");
    //    shiftEntityODataOptions.OdataEntitySet<CompanyListDTO>($"{Core.Constants.IdentityRoutePrefix}Company");
    //    shiftEntityODataOptions.OdataEntitySet<CompanyBranchListDTO>($"{Core.Constants.IdentityRoutePrefix}CompanyBranch");

    //    return shiftEntityODataOptions;
    //}
}
