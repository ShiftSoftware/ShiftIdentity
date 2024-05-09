using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Service;

public class ServiceDTO : ShiftEntityViewAndUpsertDTO
{
    [ServiceHashIdConverter]
    public override string? ID { get; set; }

    [Required]
    public string Name { get; set; } = default!;

    public string? IntegrationId { get; set; }
}
