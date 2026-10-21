using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Application.Compose;

/// <summary><paramref name="VisibleAccountIds"/> null = ve todo; si no, oculta enviados desde buzones no visibles (gate de buzón de oficina).</summary>
public sealed record ListSentMessagesQuery(
    Guid TenantId,
    DraftCustomerScope Customer,
    int Page,
    int Size,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null
);
