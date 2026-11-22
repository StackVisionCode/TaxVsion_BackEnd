using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Domain.Catalog;

/// <summary>Quién pregunta por sus preferencias: alguien de la oficina o una cuenta del portal.</summary>
public enum PreferenceAudience
{
    Staff,
    CustomerPortal,
}

/// <summary>
/// Qué interruptor de preferencias hace algo, calculado del catálogo. Una celda está disponible si al menos un
/// kind con esa categoría de preferencia permite el canal, no es obligatorio en él y va a ese tipo de actor.
/// </summary>
public sealed class NotificationPreferenceAvailability
{
    private readonly Dictionary<(NotificationCategory, NotificationChannel, PreferenceAudience), Cell> _cells = [];

    public NotificationPreferenceAvailability(IEnumerable<NotificationCatalogEntry> entries)
    {
        var all = entries.ToArray();
        foreach (var category in Enum.GetValues<NotificationCategory>())
        foreach (var channel in Enum.GetValues<NotificationChannel>())
        foreach (var audience in Enum.GetValues<PreferenceAudience>())
        {
            var reaching = all.Where(entry =>
                    entry.PreferenceCategory == category
                    && entry.AllowedChannels.Contains(channel)
                    && AudienceOf(entry) == audience
                )
                .ToArray();
            var switchable = reaching.Where(entry => !entry.IsMandatoryFor(channel)).ToArray();
            // Una celda bloqueada describe todo lo que pasa por ella; una libre, solo lo que su interruptor apaga.
            var described = NotificationCategoryRules.IsLocked(category) ? reaching : switchable;
            var covers = described
                .Select(entry => NotificationContractNames.For(entry.ResourceType))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            _cells[(category, channel, audience)] = new Cell(switchable.Length > 0, covers);
        }
    }

    public bool IsAvailable(NotificationCategory category, NotificationChannel channel, PreferenceAudience audience) =>
        _cells[(category, channel, audience)].Available;

    /// <summary>Claves cortas de lo que incluye la celda (el tipo de recurso); los textos están en el frontend.</summary>
    public IReadOnlyList<string> Covers(
        NotificationCategory category,
        NotificationChannel channel,
        PreferenceAudience audience
    ) => _cells[(category, channel, audience)].Covers;

    private static PreferenceAudience AudienceOf(NotificationCatalogEntry entry) =>
        entry.RecipientPolicy == NotificationRecipientPolicy.CustomerPortal
            ? PreferenceAudience.CustomerPortal
            : PreferenceAudience.Staff;

    private sealed record Cell(bool Available, IReadOnlyList<string> Covers);
}
