using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Infrastructure.Feedback.Smtp2Go;

/// <summary>
/// El único lugar donde vive el vocabulario de SMTP2GO. El webhook dice "bounce" + "hard"; la API de
/// actividad dice "hard-bounced": se normaliza y las dos variantes llegan al mismo outcome.
/// </summary>
internal static class Smtp2GoEventVocabulary
{
    /// <summary>Ambos null = evento que no nos interesa (processed, resubscribe…).</summary>
    public static (DeliveryOutcome? Outcome, EngagementSignal? Signal) Translate(
        string? eventName,
        string? bounceKind
    ) =>
        Normalize(eventName) switch
        {
            "delivered" => (DeliveryOutcome.Delivered, null),
            // Sin tipo de rebote se trata como blando: un duro suprime la dirección y no queremos adivinar.
            "bounce" or "bounced" => (
                string.Equals(bounceKind?.Trim(), "hard", StringComparison.OrdinalIgnoreCase)
                    ? DeliveryOutcome.HardBounced
                    : DeliveryOutcome.SoftBounced,
                null
            ),
            "hardbounce" or "hardbounced" => (DeliveryOutcome.HardBounced, null),
            "softbounce" or "softbounced" => (DeliveryOutcome.SoftBounced, null),
            "reject" or "rejected" or "refused" => (DeliveryOutcome.Rejected, null),
            "spam" or "spamcomplaint" or "complaint" => (DeliveryOutcome.Complained, null),
            "open" or "opened" => (null, EngagementSignal.Opened),
            "click" or "clicked" => (null, EngagementSignal.Clicked),
            "unsubscribe" or "unsubscribed" => (null, EngagementSignal.Unsubscribed),
            // processed ya lo sabemos por la respuesta 250 del relay.
            _ => (null, null),
        };

    private static string Normalize(string? eventName) =>
        eventName is null
            ? string.Empty
            : eventName.Trim().ToLowerInvariant().Replace("-", "").Replace("_", "").Replace(" ", "");
}
