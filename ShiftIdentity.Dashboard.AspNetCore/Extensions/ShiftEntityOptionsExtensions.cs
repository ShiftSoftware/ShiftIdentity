using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Data;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions
{
    public static class ShiftEntityOptionsExtensions
    {
        [Obsolete("This method is deprecated. Please use AddShiftIdentityAutoMapper with correct namespace.")]
        public static ShiftEntityOptions AddShiftIdentityAutoMapper(this ShiftEntityOptions shiftEntityOptions)
        {
            shiftEntityOptions.AddAutoMapper(typeof(Marker).Assembly);

            return shiftEntityOptions;
        }
    }
}

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extensions
{
    public static class ShiftEntityOptionsExtensions
    {
        public static ShiftEntityOptions AddShiftIdentityAutoMapper(this ShiftEntityOptions shiftEntityOptions)
        {
            shiftEntityOptions.AddAutoMapper(typeof(Marker).Assembly);

            return shiftEntityOptions;
        }
    }
}