using System.Collections.Concurrent;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;

namespace ShiftIdentity.Tests.Infrastructure;

/// <summary>Local-only providers for testing the framework's default sink through a real host registration.</summary>
public sealed class HostSecurityInbox : ISendEmailVerification, ISendEmailResetPassword
{
    public sealed record Delivery(bool Verification, string Link, UserDataDTO User);
    private readonly ConcurrentQueue<Delivery> messages = new();
    public Delivery[] Messages => messages.ToArray();
    public Func<Delivery, bool>? Refuse { get; set; }
    public Task SendEmailVerificationAsync(string url, UserDataDTO user) => Accept(true, url, user);
    public Task SendEmailResetPasswordAsync(string url, UserDataDTO user) => Accept(false, url, user);
    private Task Accept(bool verification, string link, UserDataDTO user)
    {
        if (user.Email?.EndsWith("@example.invalid", StringComparison.OrdinalIgnoreCase) != true)
            throw new InvalidOperationException("The test inbox accepts reserved synthetic addresses only.");
        var message = new Delivery(verification, link, user);
        if (Refuse?.Invoke(message) == true) throw new InvalidOperationException("Synthetic provider refusal");
        messages.Enqueue(message);
        return Task.CompletedTask;
    }
}
