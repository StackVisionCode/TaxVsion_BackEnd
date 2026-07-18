namespace TaxVision.Correspondence.Application.Compose;

public sealed record DiscardDraftCommand(Guid TenantId, Guid DraftId);
