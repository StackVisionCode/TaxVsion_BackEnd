namespace TaxVision.Correspondence.Application.Threads;

public sealed record ListThreadMessagesQuery(Guid TenantId, Guid ThreadId, int Page, int Size);
