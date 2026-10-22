namespace TaxVision.Postmaster.Application.Feedback.Commands.ProcessDeliveryFeedback;

/// <summary>Un payload crudo de un proveedor (webhook) para traducir y aplicar.</summary>
/// <param name="Source">Nombre del origen (<c>FeedbackSource.Value</c>), ej. "Smtp2Go".</param>
/// <param name="ExpectedTenantId">
/// Si el aviso llegó al buzón de una oficina, solo puede tocar correos de esa oficina. Null para el
/// proveedor del sistema, que no sabe de oficinas.
/// </param>
public sealed record ProcessDeliveryFeedbackCommand(
    string Source,
    string RawPayload,
    DateTime ReceivedAtUtc,
    Guid? ExpectedTenantId = null
);
