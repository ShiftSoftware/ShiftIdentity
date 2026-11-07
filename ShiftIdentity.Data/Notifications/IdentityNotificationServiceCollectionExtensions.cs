using Microsoft.Extensions.DependencyInjection.Extensions;
using ShiftSoftware.ShiftEntity.Core.Notifications;
using ShiftSoftware.ShiftIdentity.Data.Notifications;

namespace Microsoft.Extensions.DependencyInjection;

public static class IdentityNotificationServiceCollectionExtensions
{
    /// <summary>
    /// Lets the notification send path read users, teams and branch tags from the replicated Identity documents. Call it
    /// next to <c>AddNotifications</c> and <c>AddIdentitySubjectResolver</c>, which registers what it reads. Without it,
    /// an event reaches only plain addresses and customers.
    /// </summary>
    public static IServiceCollection AddIdentityNotificationDirectory(this IServiceCollection services)
    {
        services.TryAddScoped<INotificationIdentityDirectory, IdentityNotificationDirectory>();
        return services;
    }
}
