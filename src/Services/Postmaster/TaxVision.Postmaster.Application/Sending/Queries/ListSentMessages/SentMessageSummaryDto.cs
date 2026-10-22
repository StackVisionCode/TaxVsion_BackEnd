namespace TaxVision.Postmaster.Application.Sending.Queries.ListSentMessages;

/// <param name="Lane">"SystemProvider" o "TenantMailbox".</param>
/// <param name="Status">Uno de <see cref="SentMessageActivityStatus"/>.</param>
public sealed record SentMessageSummaryDto(
    Guid Id,
    string Subject,
    IReadOnlyList<string> Recipients,
    string Lane,
    string Status,
    DateTime QueuedAtUtc,
    DateTime? SentAtUtc,
    DateTime? LastEventAtUtc,
    bool Opened,
    bool Clicked
);
