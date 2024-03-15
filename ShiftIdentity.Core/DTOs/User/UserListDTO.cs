using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using System;
using System.Text.Json.Serialization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.User;

[ShiftEntityKeyAndName(nameof(ID), nameof(Username))]
public class UserListDTO : ShiftEntityListDTO
{
    [UserHashIdConverter]
    public override string? ID { get; set; }

    [JsonConverter(typeof(LocalizedTextJsonConverter))]
    public string? CompanyBranch { get; set; }
    public string FullName { get; set; } = default!;
    public string Username { get; set; } = default!;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset LastSeen { get; set; }
}
