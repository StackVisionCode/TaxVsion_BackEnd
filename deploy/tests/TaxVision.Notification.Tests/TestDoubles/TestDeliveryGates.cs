using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>Gate de entrega para los tests: abierto (todo habilitado) o con fakes propios.</summary>
internal static class TestDeliveryGates
{
    public static NotificationDeliveryGate Open() =>
        For(new AllEnabledPreferences(), new InMemoryUserEmailDirectoryRepository());

    public static NotificationDeliveryGate For(
        IUserNotificationPreferenceRepository preferences,
        IUserEmailDirectoryRepository directory
    ) => new(preferences, directory, NullLogger<NotificationDeliveryGate>.Instance);

    private sealed class AllEnabledPreferences : IUserNotificationPreferenceRepository
    {
        public Task<bool> IsEnabledAsync(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel,
            CancellationToken ct = default
        ) => Task.FromResult(true);

        public Task<UserNotificationPreference?> GetAsync(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel,
            CancellationToken ct = default
        ) => Task.FromResult<UserNotificationPreference?>(null);

        public Task<IReadOnlyList<UserNotificationPreference>> ListForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<UserNotificationPreference>>([]);

        public Task AddAsync(UserNotificationPreference preference, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
