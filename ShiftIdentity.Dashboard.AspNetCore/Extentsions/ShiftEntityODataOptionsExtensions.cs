using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftIdentity.Core.DTOs.App;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions;

public static class ShiftEntityODataOptionsExtensions
{
    public static ShiftEntityODataOptions RegisterShiftIdentityDashboardEntitySets(this ShiftEntityODataOptions shiftEntityODataOptions)
    {
        shiftEntityODataOptions.OdataEntitySet<AppDTO>("App");
        shiftEntityODataOptions.OdataEntitySet<AccessTreeDTO>("AccessTree");
        shiftEntityODataOptions.OdataEntitySet<UserListDTO>("User");
        shiftEntityODataOptions.OdataEntitySet<PublicUserListDTO>("PublicUser");

        return shiftEntityODataOptions;
    }
}
