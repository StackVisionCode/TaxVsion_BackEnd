namespace TaxVision.CloudStorage.Application.Abstractions;

/// <summary>Desglose de la cuota de la oficina por categoría (columna calculada) y papelera, con GROUP BY en vivo.</summary>
public interface IStorageBreakdownReader
{
    Task<StorageBreakdown> ReadAsync(Guid tenantId, CancellationToken ct);
}

/// <param name="ByCategory">Siempre las 4 categorías del CRM, solo archivos <c>Available</c>.</param>
/// <param name="Trash">En la papelera y todavía contando como usado (estado previo <c>Available</c>).</param>
public sealed record StorageBreakdown(IReadOnlyList<CategoryUsage> ByCategory, TrashUsage Trash);

public sealed record CategoryUsage(string Category, int Count, long Bytes);

public sealed record TrashUsage(int Count, long Bytes);
