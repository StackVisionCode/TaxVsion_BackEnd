using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Application.Trash;

/// <summary><paramref name="VisibleAccountIds"/> null = ve todo; si no, oculta correo borrado de buzones no visibles (gate de buzón de oficina).</summary>
public sealed record ListTrashQuery(
    Guid TenantId,
    DraftCustomerScope Customer,
    int Page,
    int Size,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null
);
