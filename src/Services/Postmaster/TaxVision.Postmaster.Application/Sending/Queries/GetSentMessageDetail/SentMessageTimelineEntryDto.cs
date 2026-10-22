namespace TaxVision.Postmaster.Application.Sending.Queries.GetSentMessageDetail;

/// <param name="Recipient">A quién se refiere el evento; null si es del mensaje entero (Queued, Sent…).</param>
public sealed record SentMessageTimelineEntryDto(
    string EventType,
    DateTime EventAtUtc,
    string? Recipient,
    string? Reason
);
