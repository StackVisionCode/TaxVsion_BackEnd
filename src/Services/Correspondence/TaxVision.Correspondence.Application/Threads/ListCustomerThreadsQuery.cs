namespace TaxVision.Correspondence.Application.Threads;

public sealed record ListCustomerThreadsQuery(Guid TenantId, Guid CustomerId, int Page, int Size);
