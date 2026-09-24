namespace TaxVision.Correspondence.Application.Threads;

/// <summary><paramref name="VisibleAccountIds"/> null = ve todo; si no, el hilo debe tener ≥1 mensaje de esos buzones o se devuelve NotFound (gate de buzón de oficina; el hilo visible se muestra entero).</summary>
public sealed record ListThreadMessagesQuery(
    Guid TenantId,
    Guid ThreadId,
    int Page,
    int Size,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null
);
