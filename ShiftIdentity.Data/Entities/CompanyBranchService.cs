using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Replication;
using System;
using System.ComponentModel.DataAnnotations.Schema;


namespace ShiftSoftware.ShiftIdentity.Data.Entities;

[TemporalShiftEntity]
[Table("CompanyBranchServices", Schema = "ShiftIdentity")]
public class CompanyBranchService : ShiftEntity<CompanyBranchService>, IShiftEntityReplication
{
    /// <inheritdoc />
    public DateTimeOffset? LastReplicationDate { get; set; }

    /// <inheritdoc />
    public string? LastReplicationStamp { get; set; }

    public long ID { get; set; }
    public long CompanyBranchID { get; set; }
    public long ServiceID { get; set; }

    public virtual Service Service { get; set; } = default!;
    public virtual CompanyBranch CompanyBranch { get; set; } = default!;
}
