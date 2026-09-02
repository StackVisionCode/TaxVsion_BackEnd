namespace TaxVision.Correspondence.Application.Threads;

public sealed record UnarchiveThreadCommand(Guid TenantId, Guid ThreadId);
