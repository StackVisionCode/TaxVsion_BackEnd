using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Delivery;

namespace TaxVision.Correspondence.Application.Delivery;

/// <summary>
/// Tarjeta de aviso de entrega (en la conversación y en la fila del Inbox). <see cref="Outcome"/>: Delayed o
/// NotDelivered; <see cref="ReasonCode"/> estable para el texto y las acciones (ver <c>DeliveryReasonCodes</c>);
/// <see cref="Diagnostic"/> es la respuesta técnica del servidor (la UI la oculta por defecto);
/// <see cref="CanViewOriginal"/> = se puede pedir el aviso original con <c>GET /delivery-notices/{id}/original</c>.
/// </summary>
public sealed record DeliveryNoticeSummary(
    Guid DeliveryNoticeId,
    Guid DraftId,
    string Recipient,
    string Outcome,
    string? Category,
    string ReasonCode,
    string? StatusCode,
    string? Diagnostic,
    bool Suppressed,
    bool CanViewOriginal,
    bool IsRead,
    DateTime OccurredAtUtc,
    DateTime ReceivedAtUtc
)
{
    public static DeliveryNoticeSummary From(DeliveryNotice notice) =>
        new(
            notice.Id,
            notice.DraftId,
            notice.Recipient,
            notice.Outcome.ToString(),
            notice.Category,
            notice.ReasonCode,
            notice.StatusCode,
            notice.Diagnostic,
            notice.Suppressed,
            notice.CanViewOriginal,
            notice.IsRead,
            notice.OccurredAtUtc,
            notice.ReceivedAtUtc
        );
}

/// <summary>Estado de entrega de UN destinatario de un enviado. <see cref="Status"/> null = enviado, sin noticias.</summary>
public sealed record RecipientDeliverySummary(
    string Address,
    string Type,
    string? Status,
    string? Category,
    DateTime? UpdatedAtUtc
);

/// <summary>Estado de entrega de un enviado para la píldora (Delivered / Delayed / NotDelivered) y su detalle.</summary>
public static class DeliveryStatusSummary
{
    public static IReadOnlyList<RecipientDeliverySummary> Recipients(Draft draft) =>
        draft
            .Recipients.Select(r => new RecipientDeliverySummary(
                r.Address,
                r.Type.ToString(),
                r.DeliveryStatus?.ToString(),
                r.DeliveryCategory,
                r.DeliveryUpdatedAtUtc
            ))
            .ToList();

    /// <summary>
    /// El peor estado entre los destinatarios con noticias: NotDelivered &gt; Delayed &gt; Delivered. Null si ninguno
    /// tiene noticias todavía (la píldora sigue diciendo "Sent").
    /// </summary>
    public static string? Overall(Draft draft)
    {
        var known = draft
            .Recipients.Where(r => r.DeliveryStatus is not null)
            .Select(r => r.DeliveryStatus!.Value)
            .ToList();
        if (known.Count == 0)
            return null;
        if (known.Contains(DeliveryOutcome.NotDelivered))
            return nameof(DeliveryOutcome.NotDelivered);
        if (known.Contains(DeliveryOutcome.Delayed))
            return nameof(DeliveryOutcome.Delayed);
        return nameof(DeliveryOutcome.Delivered);
    }
}
