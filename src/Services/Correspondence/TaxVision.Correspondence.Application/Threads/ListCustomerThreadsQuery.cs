namespace TaxVision.Correspondence.Application.Threads;

/// <summary><paramref name="VisibleAccountIds"/> null = ve todo; si no, oculta hilos sin ningún mensaje de esos buzones (gate de buzón de oficina).</summary>
public sealed record ListCustomerThreadsQuery(
    Guid TenantId,
    Guid CustomerId,
    int Page,
    int Size,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null
);
