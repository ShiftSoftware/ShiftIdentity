using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.Flags;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("Countries", Schema = "ShiftIdentity")]
public class Country : ShiftEntity<Country>, IEntityHasCountry<Country>
{
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
    public string? ShortCode { get; set; }
    public string CallingCode { get; set; } = default!;
    public bool BuiltIn { get; set; }
    public virtual IEnumerable<Region> Regions { get; set; }
    public long? CountryID { get; set; }
    public string? Flag { get; set; }

    public int? DisplayOrder { get; set; }

    public Country()
    {
        Regions = new HashSet<Region>();
    }
}
