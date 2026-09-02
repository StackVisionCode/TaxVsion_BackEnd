namespace TaxVision.Correspondence.Application.Compose;

public sealed record ListSentMessagesQuery(Guid TenantId, Guid CustomerId, int Page, int Size);
