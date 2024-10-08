﻿using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Core.Flags;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("Regions", Schema = "ShiftIdentity")]
[DontSetCompanyInfoOnThisEntityWithAutoTrigger]
public class Region : ShiftEntity<Region>, IEntityHasCountry<Region>
{
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
    public string? ShortCode { get; set; }
    public bool BuiltIn { get; set; }
    public long? CountryID { get; set; }
    public virtual Country? Country { get; set; }

    public virtual ICollection<CompanyBranch> CompanyBranches { get; set; }

    public Region()
    {
        CompanyBranches = new HashSet<CompanyBranch>();
    }

}
