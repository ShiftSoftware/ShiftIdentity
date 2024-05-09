using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Service;

[ShiftEntityKeyAndName(nameof(ID), nameof(Name))]
public class ServiceListDTO : ShiftEntityListDTO
{
    [ServiceHashIdConverter]
    public override string? ID { get; set; }
    public string Name { get; set; } = default!;

    public string? IntegrationId { get; set; }
}
