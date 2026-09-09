using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Authentication;

namespace ShiftIdentity.Tests.Infrastructure;

/// <summary>Development/test sink only. It accepts reserved synthetic destinations and never sends mail.</summary>
public sealed class LocalSecurityInbox(TimeProvider? clock = null) : ISecurityEmailSink
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly Dictionary<Guid, LocalSecurityMessage> accepted = [];
    private int failDeliveries;
    private int failAfterAccept;
    private int deliveryCalls;

    public bool FailDeliveries { get => Volatile.Read(ref failDeliveries) != 0; set => Volatile.Write(ref failDeliveries, value ? 1 : 0); }
    // Simulates a sender that accepted the message but failed before confirming acceptance to its caller.
    public bool FailAfterAccept { get => Volatile.Read(ref failAfterAccept) != 0; set => Volatile.Write(ref failAfterAccept, value ? 1 : 0); }
    public int DeliveryCalls => Volatile.Read(ref deliveryCalls);
    public LocalSecurityMessage[] Messages { get { lock (gate) return accepted.Values.OrderByDescending(x => x.DeliveredAt).ToArray(); } }

    public Task DeliverAsync(SecurityEmail message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref deliveryCalls);
        if (!(message.Destination.EndsWith("@example.invalid", StringComparison.OrdinalIgnoreCase) ||
              message.Destination.EndsWith(".example.invalid", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The local inbox accepts only reserved synthetic destinations.");
        if (message.Purpose is not (AuthenticationOperationPurpose.PasswordResetEmail or AuthenticationOperationPurpose.EmailVerify))
            throw new InvalidOperationException("The local inbox accepts only email delivery purposes.");
        if (FailDeliveries) throw new InvalidOperationException("Synthetic delivery failure.");
        var route = message.Purpose == AuthenticationOperationPurpose.EmailVerify ? "/Identity/VerifyEmail" : "/Identity/ResetPassword";
        var link = route + "#grant=" + Uri.EscapeDataString(message.Grant) + "&purpose=" + message.Purpose;
        lock (gate)
            accepted.TryAdd(message.ID, new(message.ID, message.Destination, message.Subject, link, clock.GetUtcNow()));
        if (FailAfterAccept) throw new InvalidOperationException("Synthetic delivery acknowledgement failure.");
        return Task.CompletedTask;
    }

}

public sealed record LocalSecurityMessage(Guid ID, string Destination, string Subject, string Link, DateTimeOffset DeliveredAt);
