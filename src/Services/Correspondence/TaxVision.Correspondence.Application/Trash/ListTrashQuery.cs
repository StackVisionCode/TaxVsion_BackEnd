namespace TaxVision.Correspondence.Application.Trash;

public sealed record ListTrashQuery(Guid TenantId, Guid CustomerId, int Page, int Size);
