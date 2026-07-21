using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Notifications.Preferences;

public sealed record NotificationPreferenceItem(
    NotificationCategory Category,
    NotificationChannel Channel,
    bool Enabled,
    bool Locked
);

public sealed record GetNotificationPreferencesQuery(Guid TenantId, Guid UserId);

/// <summary>
/// Devuelve el producto completo Categoría×Canal (ver <see cref="NotificationCategory"/>): las
/// combinaciones sin fila propia salen con el default (Enabled=true, opt-out). Las categorías
/// locked (hoy solo AccountSecurity) nunca se leen de la tabla — se devuelven siempre
/// Enabled=true/Locked=true, el frontend las muestra con el interruptor deshabilitado.
/// </summary>
public static class GetNotificationPreferencesHandler
{
    public static async Task<Result<IReadOnlyList<NotificationPreferenceItem>>> Handle(
        GetNotificationPreferencesQuery query,
        IUserNotificationPreferenceRepository repository,
        CancellationToken ct
    )
    {
        var stored = await repository.ListForUserAsync(query.TenantId, query.UserId, ct);
        var storedByKey = stored.ToDictionary(p => (p.Category, p.Channel), p => p.Enabled);

        var items = new List<NotificationPreferenceItem>();
        foreach (var category in Enum.GetValues<NotificationCategory>())
        {
            var locked = NotificationCategoryRules.IsLocked(category);
            foreach (var channel in Enum.GetValues<NotificationChannel>())
            {
                var enabled =
                    locked || !storedByKey.TryGetValue((category, channel), out var stateEnabled) || stateEnabled;
                items.Add(new NotificationPreferenceItem(category, channel, enabled, locked));
            }
        }

        return Result.Success<IReadOnlyList<NotificationPreferenceItem>>(items);
    }
}
