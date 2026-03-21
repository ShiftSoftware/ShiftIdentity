using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyCalendar;

public class CalendarFilterDTO
{
    public DateTime ViewStartDate { get; set; }
    public DateTime ViewEndDate { get; set; }

    /// <summary>Hash ID of the selected company. Sent by the client.</summary>
    public string? CompanyHashId { get; set; }

    /// <summary>Hash IDs of selected branches. Sent by the client.</summary>
    public List<string>? ViewBranchHashIds { get; set; }

    /// <summary>Hash IDs of selected departments. Sent by the client.</summary>
    public List<string>? ViewDepartmentHashIds { get; set; }

    /// <summary>Hash IDs of selected brands. Sent by the client.</summary>
    public List<string>? ViewBrandHashIds { get; set; }

    /// <summary>Decoded company ID. Populated server-side from CompanyHashId.</summary>
    [JsonIgnore]
    public long? CompanyId { get; set; }

    /// <summary>Decoded branch IDs. Populated server-side from ViewBranchHashIds.</summary>
    [JsonIgnore]
    public List<long>? ViewBranchIds { get; set; }

    /// <summary>Decoded department IDs. Populated server-side from ViewDepartmentHashIds.</summary>
    [JsonIgnore]
    public List<long>? ViewDepartmentIds { get; set; }

    /// <summary>Decoded brand IDs. Populated server-side from ViewBrandHashIds.</summary>
    [JsonIgnore]
    public List<long>? ViewBrandIds { get; set; }
}
