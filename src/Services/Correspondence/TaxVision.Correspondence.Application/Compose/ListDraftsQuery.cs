namespace TaxVision.Correspondence.Application.Compose;

public sealed record ListDraftsQuery(Guid TenantId, Guid CustomerId, int Page, int Size);
