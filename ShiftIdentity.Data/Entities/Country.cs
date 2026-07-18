using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.Flags;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Country;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

// Attribute-driven endpoint (Rung A): Country has no controller and no repository class. The secure CRUD
// routes are generated from the attribute below (built-in repository + source-generated mapper), gated by the
// ShiftIdentityActions.Countries TypeAuth node. The protected-row guard (IsProtected) is enforced centrally by
// the built-in repository, and feature locking by FeatureLockSaveValidator, so no repository overrides remain.
// The route reproduces the old IdentityCountryController's "api/[controller]" output exactly.
[TemporalShiftEntity]
[Table("Countries", Schema = "ShiftIdentity")]
[ShiftEntitySecureEndpoint<CountryListDTO, CountryDTO, ShiftIdentityActions>("api/IdentityCountry", nameof(ShiftIdentityActions.Countries), UseGeneratedMapper = true)]
public class Country : ShiftEntity<Country>, IEntityHasCountry<Country>, IShiftEntityReplication, IShiftEntityProtectable
{
    /// <inheritdoc />
    public DateTimeOffset? LastReplicationDate { get; set; }

    /// <inheritdoc />
    public string? LastReplicationStamp { get; set; }

    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
    public string? ShortCode { get; set; }
    public string CallingCode { get; set; } = default!;
    public bool IsProtected { get; set; }
    public virtual IEnumerable<Region> Regions { get; set; }
    public long? CountryID { get; set; }
    public string? Flag { get; set; }

    public int? DisplayOrder { get; set; }

    public Country()
    {
        Regions = new HashSet<Region>();
    }
}
