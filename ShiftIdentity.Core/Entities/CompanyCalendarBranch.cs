using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[Table("CompanyCalendarBranches", Schema = "ShiftIdentity")]
public class CompanyCalendarBranch
{
    public long Id { get; set; }
    public long CompanyCalendarID { get; set; }
    public CompanyCalendar CompanyCalendar { get; set; } = default!;
    public long CompanyBranchID { get; set; }
}
