using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Flags;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Brand;
using System;
using System.ComponentModel.DataAnnotations.Schema;

using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

// Attribute-driven endpoint: Brand has no controller and no repository class. The secure CRUD routes are
// generated from the attribute below (built-in repository + source-generated mapper), gated by the
// ShiftIdentityActions.Brands TypeAuth node. Feature locking is enforced centrally by FeatureLockSaveValidator,
// so no SaveChangesAsync override is needed. DI is wired by RegisterShiftRepositories(...) inside
// AddShiftIdentityDashboard; the routes are mapped by the host's app.MapShiftEntityEndpoints<DB>() once it
// registers this assembly via AddShiftIdentityDataAssembly(). The route string reproduces the old
// IdentityBrandController's "api/[controller]" output exactly, so clients are unaffected.
[TemporalShiftEntity]
[Table("Brands", Schema = "ShiftIdentity")]
[ShiftEntityKeyAndName(nameof(ID), nameof(Name))]
[ShiftEntitySecureEndpoint<BrandListDTO, BrandDTO, ShiftIdentityActions>("api/IdentityBrand", nameof(ShiftIdentityActions.Brands), UseGeneratedMapper = true)]
public class Brand : ShiftEntity<Brand>, IEntityHasBrand<Brand>, IShiftEntityReplication
{
    /// <inheritdoc />
    public DateTimeOffset? LastReplicationDate { get; set; }

    /// <inheritdoc />
    public string? LastReplicationStamp { get; set; }

    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
    public long? BrandID { get; set; }
}
