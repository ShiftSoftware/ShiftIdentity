using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Data;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions;

public static class ShiftEntityOptionsExtensions
{
    public static ShiftEntityOptions AddShiftIdentityAutoMapper(this ShiftEntityOptions shiftEntityOptions)
    {
        shiftEntityOptions.AddAutoMapper(typeof(Marker).Assembly);

        return shiftEntityOptions;
    }

    /// <summary>
    /// Registers the ShiftIdentity.Data assembly as a data assembly, so the host's
    /// <c>app.MapShiftEntityEndpoints&lt;DB&gt;()</c> discovers ShiftIdentity's attribute-driven CRUD endpoints
    /// (the entities carrying <c>[ShiftEntitySecureEndpoint&lt;…&gt;]</c>, e.g. Brand) and maps their routes.
    /// The entities live in ShiftIdentity.Data (alongside the DbContext + EF Core), so the generator and
    /// endpoint discovery both key off that assembly. The DI half is wired for you inside
    /// <c>AddShiftIdentityDashboard(...)</c>.
    /// </summary>
    public static ShiftEntityOptions AddShiftIdentityDataAssembly(this ShiftEntityOptions shiftEntityOptions)
    {
        shiftEntityOptions.AddDataAssembly(typeof(Marker).Assembly);

        return shiftEntityOptions;
    }
}
