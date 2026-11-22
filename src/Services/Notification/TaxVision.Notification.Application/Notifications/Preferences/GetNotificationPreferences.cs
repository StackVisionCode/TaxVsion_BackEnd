using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Notifications.Preferences;

public sealed record NotificationPreferenceItem(
    NotificationCategory Category,
    NotificationChannel Channel,
    bool Enabled,
    bool Locked,
    // Hay al menos un aviso que este interruptor apaga (o la celda está bloqueada).
    bool Available = true,
    IReadOnlyList<string>? Covers = null
);

public sealed record GetNotificationPreferencesQuery(
    Guid TenantId,
    Guid UserId,
    PreferenceAudience Audience = PreferenceAudience.Staff
);

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
        // El portal no tiene pantalla de preferencias.
        if (query.Audience == PreferenceAudience.CustomerPortal)
            return Result.Success<IReadOnlyList<NotificationPreferenceItem>>([]);

        var stored = await repository.ListForUserAsync(query.TenantId, query.UserId, ct);
        return Result.Success(Build(query, stored, NotificationCatalog.Availability));
    }

    public static IReadOnlyList<NotificationPreferenceItem> Build(
        GetNotificationPreferencesQuery query,
        IReadOnlyList<UserNotificationPreference> stored,
        NotificationPreferenceAvailability availability
    )
    {
        var storedByKey = stored.ToDictionary(p => (p.Category, p.Channel), p => p.Enabled);

        var items = new List<NotificationPreferenceItem>();
        foreach (var category in Enum.GetValues<NotificationCategory>())
        {
            var locked = NotificationCategoryRules.IsLocked(category);
            foreach (var channel in Enum.GetValues<NotificationChannel>())
            {
                var enabled =
                    locked || !storedByKey.TryGetValue((category, channel), out var stateEnabled) || stateEnabled;
                items.Add(
                    new NotificationPreferenceItem(
                        category,
                        channel,
                        enabled,
                        locked,
                        locked || availability.IsAvailable(category, channel, query.Audience),
                        availability.Covers(category, channel, query.Audience)
                    )
                );
            }
        }

        return items;
    }
}
