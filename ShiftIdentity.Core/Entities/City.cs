using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core.ReplicationModels;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;


[TemporalShiftEntity]
[Table("Cities", Schema = "ShiftIdentity")]
[DontSetCompanyInfoOnThisEntityWithAutoTrigger]
[ShiftEntityReplication<CityModel>(ContainerName = ReplicationConfiguration.RegionContainerName,
    AccountName = ReplicationConfiguration.AccountName)]
[ReplicationPartitionKey(nameof(CityModel.RegionID), nameof(CityModel.ItemType))]
[PropertyReferenceReplication<CityModel>(ReplicationConfiguration.CompanyBranchContainerName, nameof(CompanyBranchModel.City))]
public class City : ShiftEntity<City>
{
    public string Name { get; set; } = default!;
    public new long RegionID { get; set; }
    public virtual Region Region { get; set; } = default!;
    public bool BuiltIn { get; set; }
    public virtual ICollection<CompanyBranch> CompanyBranches { get; set; }
    public City()
    {
        CompanyBranches = new HashSet<CompanyBranch>();
    }
}