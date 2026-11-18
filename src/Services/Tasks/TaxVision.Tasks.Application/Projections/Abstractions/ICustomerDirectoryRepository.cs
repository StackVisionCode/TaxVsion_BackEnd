using TaxVision.Tasks.Domain.Projections;

namespace TaxVision.Tasks.Application.Projections.Abstractions;

/// <summary>Ficha de un cliente importado, lista para el directorio (email ya normalizado o nulo).</summary>
public sealed record ImportedDirectoryEntry(
    Guid CustomerId,
    string DisplayName,
    string? PrimaryEmailNormalized,
    CustomerDirectoryStatus Status
);

public interface ICustomerDirectoryRepository
{
    Task<bool> ExistsAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    Task<string?> GetDisplayNameAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    Task<CustomerDirectoryEntry?> GetByCustomerIdAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    Task AddAsync(CustomerDirectoryEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Upsert set-based por <c>MERGE</c> de un bloque del import (hasta 500): nunca carga N entidades. Crea las filas
    /// que faltan y actualiza las existentes solo si no tienen un cambio más nuevo que <paramref name="observedAtUtc"/>
    /// (misma regla que <see cref="CustomerDirectoryEntry.ApplyIfNewer"/>). Un email nulo no borra el conocido.
    /// </summary>
    Task UpsertImportedAsync(
        Guid tenantId,
        IReadOnlyList<ImportedDirectoryEntry> entries,
        DateTime observedAtUtc,
        CancellationToken ct = default
    );

    /// <summary>Tenants con al menos una fila sin nombre — el universo que recorre el job de reconciliación.</summary>
    Task<IReadOnlyList<Guid>> ListTenantIdsWithMissingNamesAsync(int limit, CancellationToken ct = default);

    /// <summary>Rellena el nombre sólo si sigue faltando; no-op si ya se conoce.</summary>
    Task ApplyDisplayNameIfMissingAsync(
        Guid tenantId,
        Guid customerId,
        string displayName,
        CancellationToken ct = default
    );
}
