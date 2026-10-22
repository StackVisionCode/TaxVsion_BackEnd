namespace TaxVision.Postmaster.Application.Sending.Queries.GetSentMessageDetail;

/// <summary>Sin el cuerpo ni el payload crudo del proveedor: solo lo que hace falta para entender qué pasó.</summary>
public sealed record SentMessageDetailDto(
    Guid Id,
    string Subject,
    string FromAddress,
    string Lane,
    string Status,
    DateTime QueuedAtUtc,
    DateTime? SentAtUtc,
    string? ErrorReason,
    IReadOnlyList<SentMessageRecipientDetailDto> Recipients,
    IReadOnlyList<SentMessageTimelineEntryDto> Timeline
);
