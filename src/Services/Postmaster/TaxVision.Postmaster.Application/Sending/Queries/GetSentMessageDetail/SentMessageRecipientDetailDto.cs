namespace TaxVision.Postmaster.Application.Sending.Queries.GetSentMessageDetail;

/// <param name="Outcome">Lo último que contó el proveedor (null si nada todavía).</param>
public sealed record SentMessageRecipientDetailDto(
    Guid Id,
    string Address,
    string Type,
    string? Outcome,
    DateTime? OutcomeAtUtc,
    string? ErrorReason,
    int OpenCount,
    DateTime? FirstOpenedAtUtc,
    int ClickCount,
    DateTime? FirstClickedAtUtc,
    DateTime? UnsubscribedAtUtc
);
