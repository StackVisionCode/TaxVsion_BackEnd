using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Common;

/// <summary>Idioma de cada destinatario mientras dura un evento: una lectura por usuario.</summary>
public sealed class RecipientLocaleCache(IRecipientLocale locales, Guid tenantId)
{
    private readonly Dictionary<Guid, RecipientLocale> byUser = [];

    public async Task<RecipientLocale> ForUserAsync(Guid userId, CancellationToken ct)
    {
        if (!byUser.TryGetValue(userId, out var locale))
            byUser[userId] = locale = await locales.ForUserAsync(tenantId, userId, ct);
        return locale;
    }
}
