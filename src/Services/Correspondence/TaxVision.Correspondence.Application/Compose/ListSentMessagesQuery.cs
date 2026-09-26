namespace TaxVision.Correspondence.Application.Compose;

/// <summary><paramref name="VisibleAccountIds"/> null = ve todo; si no, oculta enviados desde buzones no visibles (gate de buzón de oficina).</summary>
public sealed record ListSentMessagesQuery(
    Guid TenantId,
    Guid CustomerId,
    int Page,
    int Size,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null
);
