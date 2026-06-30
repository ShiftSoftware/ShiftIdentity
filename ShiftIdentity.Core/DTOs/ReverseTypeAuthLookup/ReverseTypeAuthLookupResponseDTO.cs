using System.Collections.Generic;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.ReverseTypeAuthLookup;

public class ReverseTypeAuthLookupResponseDTO
{
    public List<ReverseTypeAuthLookupUserDTO> Users { get; set; } = new();

    public int TotalMatchingUsers { get; set; }

    public int SuperAdminCount { get; set; }

    /// <summary>
    /// Named access trees that grant the requested access on the action.
    /// </summary>
    public List<ReverseTypeAuthLookupAccessTreeDTO> AccessTrees { get; set; } = new();

    public int TotalMatchingAccessTrees { get; set; }

    /// <summary>
    /// Friendly display name for the action (e.g. "Identity › Users").
    /// </summary>
    public string? ActionDisplayName { get; set; }
}
