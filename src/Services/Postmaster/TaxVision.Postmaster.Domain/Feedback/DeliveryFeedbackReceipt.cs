using BuildingBlocks.Domain;

namespace TaxVision.Postmaster.Domain.Feedback;

/// <summary>
/// Recibo de un aviso de entrega ya aplicado. El índice único (Source, SourceEventId) evita aplicarlo
/// dos veces cuando el proveedor reintenta o dos copias llegan juntas.
/// </summary>
public sealed class DeliveryFeedbackReceipt : BaseEntity
{
    private DeliveryFeedbackReceipt() { }

    public string Source { get; private set; } = default!;
    public string SourceEventId { get; private set; } = default!;
    public Guid SentMessageId { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }

    public static DeliveryFeedbackReceipt Create(
        string source,
        string sourceEventId,
        Guid sentMessageId,
        DateTime receivedAtUtc
    ) =>
        new()
        {
            Source = source,
            SourceEventId = sourceEventId,
            SentMessageId = sentMessageId,
            ReceivedAtUtc = receivedAtUtc,
        };
}
