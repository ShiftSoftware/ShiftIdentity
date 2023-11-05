
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftEntity.Model.Replication;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Region;

[ReplicationPartitionKey(nameof(RegionDTO.ID))]
public class RegionDTO : ShiftEntityViewAndUpsertDTO
{
    [RegionHashIdConverter]
    public override string? ID { get; set; }

    [Required]
    public string Name { get; set; } = default!;
    public string? ExternalId { get; set; } = default!;
    public string? ShortCode { get; set; }
}
