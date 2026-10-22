using BuildingBlocks.Domain;

namespace TaxVision.Postmaster.Domain.Feedback;

/// <summary>
/// Aviso de entrega que no pudimos atar a ningún correo nuestro. Se guarda 30 días solo para
/// diagnóstico; la dirección va enmascarada.
/// </summary>
public sealed class UnmatchedDeliveryFeedback : BaseEntity
{
    public const int MaxRawPayloadLength = 8192;

    private UnmatchedDeliveryFeedback() { }

    public string Source { get; private set; } = default!;
    public string SourceEventId { get; private set; } = default!;
    public string? MaskedRecipient { get; private set; }

    /// <summary>El id con el que intentamos correlacionar (nuestro Message-ID o el del proveedor).</summary>
    public string? CorrelationHint { get; private set; }
    public string? RawPayload { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }

    public static UnmatchedDeliveryFeedback Create(
        string source,
        string sourceEventId,
        string? maskedRecipient,
        string? correlationHint,
        string? rawPayload,
        DateTime receivedAtUtc
    ) =>
        new()
        {
            Source = source,
            SourceEventId = sourceEventId,
            MaskedRecipient = maskedRecipient is { Length: > 320 } ? maskedRecipient[..320] : maskedRecipient,
            CorrelationHint = correlationHint is { Length: > 500 } ? correlationHint[..500] : correlationHint,
            RawPayload = rawPayload is { Length: > MaxRawPayloadLength }
                ? rawPayload[..MaxRawPayloadLength]
                : rawPayload,
            ReceivedAtUtc = receivedAtUtc,
        };
}
