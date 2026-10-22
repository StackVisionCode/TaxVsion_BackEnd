using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Application.Feedback;

/// <summary>
/// Un aviso del proveedor ya traducido por su adaptador. Trae los tres ids con los que se puede atar a
/// nuestro correo; cualquiera puede venir vacío.
/// </summary>
/// <param name="MessageId">Message-ID tal como lo devolvió el proveedor (si es nuestro, empieza con "pm.").</param>
/// <param name="CorrelationHeader">Valor de <c>X-TaxVision-Message-Id</c>, si el proveedor lo devuelve.</param>
/// <param name="ProviderMessageId">Id propio del proveedor para ese correo.</param>
/// <param name="RawPayload">El pedazo del payload que describe este aviso, solo para auditoría.</param>
public sealed record TranslatedFeedback(
    string? MessageId,
    string? CorrelationHeader,
    string? ProviderMessageId,
    string RecipientAddress,
    DeliveryFeedback Feedback,
    string RawPayload
);
