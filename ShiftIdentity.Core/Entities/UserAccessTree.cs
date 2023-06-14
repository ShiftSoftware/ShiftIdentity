namespace ShiftSoftware.ShiftIdentity.Core.Entities;

public class UserAccessTree
{
    public long ID { get; set; }
    public long UserID { get; set; }

    public long AccessTreeID { get; set; }

    public User User { get; set; } = default!;

    public AccessTree AccessTree { get; set; } = default!;
}
