using TaxVision.Notes.Domain.Projections;

namespace TaxVision.Notes.Application.Projections.Abstractions;

/// <summary>
/// Proyección local de customers del tenant (Fase 4B). Consultada por Fase 5 (Application) para
/// la validación SOFT de <c>CreateNoteCommand</c> cuando <c>TargetType==Customer</c>.
/// </summary>
/// <summary>Ficha de un cliente importado, lista para el directorio.</summary>
public sealed record ImportedDirectoryEntry(Guid CustomerId, string DisplayName, CustomerDirectoryStatus Status);

public interface ICustomerDirectoryRepository
{
    Task<bool> ExistsAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    Task<string?> GetDisplayNameAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    Task<CustomerDirectoryEntry?> GetByCustomerIdAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    Task AddAsync(CustomerDirectoryEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Upsert set-based por <c>MERGE</c> de un bloque del import (hasta 500): nunca carga N entidades. Crea las filas
    /// que faltan y actualiza las existentes solo si no tienen un cambio más nuevo que <paramref name="observedAtUtc"/>
    /// (misma regla que <see cref="CustomerDirectoryEntry.ApplyIfNewer"/>).
    /// </summary>
    Task UpsertImportedAsync(
        Guid tenantId,
        IReadOnlyList<ImportedDirectoryEntry> entries,
        DateTime observedAtUtc,
        CancellationToken ct = default
    );

    /// <summary>Tenants con al menos una fila <c>DisplayName IS NULL</c> — universo que el job de reconciliación debe recorrer.</summary>
    Task<IReadOnlyList<Guid>> ListTenantIdsWithMissingNamesAsync(int limit, CancellationToken ct = default);

    /// <summary>Reconciliación (job de background): rellena el nombre solo si sigue faltando. No-op si ya se conoce.</summary>
    Task ApplyDisplayNameIfMissingAsync(
        Guid tenantId,
        Guid customerId,
        string displayName,
        CancellationToken ct = default
    );
}
