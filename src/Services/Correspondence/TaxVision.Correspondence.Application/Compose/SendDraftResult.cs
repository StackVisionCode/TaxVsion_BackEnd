namespace TaxVision.Correspondence.Application.Compose;

/// <param name="InProgress">El envío sigue en curso en Postmaster; el borrador queda en Sending (COR-17).</param>
public sealed record SendDraftResult(Guid SentMessageId, string? ProviderMessageId, bool InProgress = false);
