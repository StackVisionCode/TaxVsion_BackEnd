namespace TaxVision.Correspondence.Application.Threads;

public sealed record ArchiveThreadCommand(Guid TenantId, Guid ThreadId);
