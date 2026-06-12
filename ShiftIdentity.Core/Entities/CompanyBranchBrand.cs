using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Replication;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("CompanyBranchBrands", Schema = "ShiftIdentity")]
public class CompanyBranchBrand : ShiftEntity<CompanyBranchBrand>, IShiftEntityReplication
{
    /// <inheritdoc />
    public DateTimeOffset? LastReplicationDate { get; set; }

    /// <inheritdoc />
    public string? LastReplicationStamp { get; set; }

    public long ID { get; set; }
    public long CompanyBranchID { get; set; }
    public long BrandID { get; set; }

    public virtual Brand Brand { get; set; } = default!;
    public virtual CompanyBranch CompanyBranch { get; set; } = default!;
}
