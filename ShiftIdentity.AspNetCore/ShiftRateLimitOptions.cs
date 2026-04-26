using System;
using System.Threading.RateLimiting;

namespace ShiftSoftware.ShiftIdentity.Core;

public class ShiftRateLimitOptions
{
    public int PermitLimit { get; set; } = 50;
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(50);
    public int QueueLimit { get; set; } = 10;
    public QueueProcessingOrder QueueProcessingOrder { get; set; } = QueueProcessingOrder.OldestFirst;
    public bool AutoReplenishment { get; set; } = true;

}
