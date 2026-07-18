namespace TaxVision.Correspondence.Application.Compose;

public sealed record SendDraftResult(Guid SentMessageId, string? ProviderMessageId);
