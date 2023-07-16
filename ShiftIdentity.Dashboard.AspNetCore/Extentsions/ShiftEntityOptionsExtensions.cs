using ShiftSoftware.ShiftEntity.Web;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions;

public static class ShiftEntityOptionsExtensions
{
    public static ShiftEntityOptions AddShiftIdentityAutoMapper(this ShiftEntityOptions shiftEntityOptions)
    {
        shiftEntityOptions.AddAutoMapper(typeof(Marker).Assembly);

        return shiftEntityOptions;
    }
}
