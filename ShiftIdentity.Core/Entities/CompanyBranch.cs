using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Core.ReplicationModels;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("CompanyBranches", Schema = "ShiftIdentity")]
[DontSetCompanyInfoOnThisEntityWithAutoTrigger]
[ShiftEntityReplication<CompanyBranchModel>(ContainerName = ReplicationConfiguration.CompanyContainerName,
    AccountName = ReplicationConfiguration.AccountName)]
[ReplicationPartitionKey(nameof(CompanyBranchModel.CompanyID), nameof(CompanyBranchModel.BranchID), 
    nameof(CompanyBranchModel.Type))]
public class CompanyBranch : ShiftEntity<CompanyBranch>
{
    public string Name { get; set; } = default!;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? ExternalId { get; set; } = default!;
    public string? ShortCode { get; set; }
    public bool BuiltIn { get; set; }
    public virtual Company Company { get; set; } = default!;
    public virtual Region Region { get; set; } = default!;
    public virtual ICollection<CompanyBranchDepartment>? CompanyBranchDepartments { get; set; }
    public virtual ICollection<CompanyBranchService>? CompanyBranchServices { get; set; }

    public new long RegionID { get; set; }
    public new long CompanyID { get; set; }

    public CompanyBranch()
    {
        CompanyBranchDepartments = new HashSet<CompanyBranchDepartment>();
        CompanyBranchServices = new HashSet<CompanyBranchService>();
    }

}
