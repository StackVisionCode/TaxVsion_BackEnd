namespace TaxVision.Correspondence.Application.Trash;

/// <summary><paramref name="VisibleAccountIds"/> null = ve todo; si no, oculta correo borrado de buzones no visibles (gate de buzón de oficina).</summary>
public sealed record ListTrashQuery(
    Guid TenantId,
    Guid CustomerId,
    int Page,
    int Size,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null
);
