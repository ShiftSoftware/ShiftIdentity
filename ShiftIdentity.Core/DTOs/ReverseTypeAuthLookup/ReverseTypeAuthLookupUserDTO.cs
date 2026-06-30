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

    /// <summary>
    /// All access trees assigned to the user (regardless of whether they grant the looked-up action).
    /// </summary>
    public List<string> AccessTrees { get; set; } = new();

    /// <summary>
    /// True when the user's own user-specific ("self") access grants the looked-up action.
    /// </summary>
    public bool GrantedBySelf { get; set; }

    /// <summary>
    /// Names of the assigned access trees that actually grant the looked-up action
    /// (a subset of <see cref="AccessTrees"/>). Combined with <see cref="GrantedBySelf"/>,
    /// this is the exact source list: self, one tree, both, or several trees.
    /// </summary>
    public List<string> GrantingAccessTrees { get; set; } = new();
}
