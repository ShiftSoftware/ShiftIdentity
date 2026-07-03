using ShiftSoftware.ShiftEntity.Model.HashIds;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.ReverseTypeAuthLookup;

public class ReverseTypeAuthLookupAccessTreeDTO
{
    [AccessTreeHashIdConverter]
    public string ID { get; set; } = default!;

    public string Name { get; set; } = default!;

    /// <summary>
    /// Number of active users this access tree is currently assigned to.
    /// </summary>
    public int AssignedUserCount { get; set; }
}
