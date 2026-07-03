using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.ReverseTypeAuthLookup;

public class ReverseTypeAuthLookupUserDTO
{
    [UserHashIdConverter]
    public string ID { get; set; } = default!;

    public string Username { get; set; } = default!;
    public string FullName { get; set; } = default!;
    public string? Email { get; set; }

    /// <summary>
    /// Branch name. Stored as localized-text JSON; the converter emits the value for the request culture.
    /// </summary>
    [JsonConverter(typeof(LocalizedTextJsonConverter))]
    public string? CompanyBranch { get; set; }

    /// <summary>
    /// All access trees assigned to the user (regardless of whether they grant the looked-up action).
    /// </summary>
    public List<string> AccessTrees { get; set; } = new();

    /// <summary>
    /// True when the access tree assigned directly to the user (the inline <c>User.AccessTree</c>,
    /// as opposed to a named/shared access tree) grants the looked-up action.
    /// "Directly" here means the assignment source, not data-level "Self/Own" scoping.
    /// </summary>
    public bool GrantedDirectly { get; set; }

    /// <summary>
    /// Names of the assigned (named) access trees that actually grant the looked-up action
    /// (a subset of <see cref="AccessTrees"/>). Combined with <see cref="GrantedDirectly"/>,
    /// this is the exact source list: the direct tree, one named tree, both, or several trees.
    /// </summary>
    public List<string> GrantingAccessTrees { get; set; } = new();
}
