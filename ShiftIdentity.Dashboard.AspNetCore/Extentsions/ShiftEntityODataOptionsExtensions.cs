using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftIdentity.Core.DTOs.App;
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

        return shiftEntityODataOptions;
    }
}
