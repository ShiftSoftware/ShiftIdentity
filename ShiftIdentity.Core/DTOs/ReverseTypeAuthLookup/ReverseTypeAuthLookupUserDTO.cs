using ShiftSoftware.ShiftEntity.Model.HashIds;
using System.Collections.Generic;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.ReverseTypeAuthLookup;

public class ReverseTypeAuthLookupUserDTO
{
    [UserHashIdConverter]
    public string ID { get; set; } = default!;

    /// <summary>
    /// Raw numeric primary key as string — for power users running SQL against the ShiftIdentity schema.
    /// </summary>
    public string RawID { get; set; } = default!;

    public string Username { get; set; } = default!;
    public string FullName { get; set; } = default!;
    public string? Email { get; set; }
    public string? CompanyBranch { get; set; }
    public bool IsSuperAdmin { get; set; }

    public List<string> AccessTrees { get; set; } = new();
}
