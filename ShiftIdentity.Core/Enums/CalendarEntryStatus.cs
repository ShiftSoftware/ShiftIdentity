namespace ShiftSoftware.ShiftIdentity.Core.Enums;

/// <summary>
/// Computed status for individual calendar entries (runtime, not persisted).
/// </summary>
public enum CalendarEntryStatus
{
    Holiday = 0,
    WorkPeriod = 1,
    Weekend = 2,
    InactiveWorkPeriod = 4,
}
