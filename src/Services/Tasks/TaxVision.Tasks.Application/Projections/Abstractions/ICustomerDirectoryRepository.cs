using TaxVision.Tasks.Domain.Projections;

namespace TaxVision.Tasks.Application.Projections.Abstractions;

public interface ICustomerDirectoryRepository
{
    Task<bool> ExistsAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    Task<string?> GetDisplayNameAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    Task<CustomerDirectoryEntry?> GetByCustomerIdAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    Task AddAsync(CustomerDirectoryEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Upsert set-based por <c>MERGE</c> para el import masivo: nunca carga N entidades ni hace un
    /// fetch por id. Las filas nuevas nacen sin <c>DisplayName</c> porque el evento no trae PII; las
    /// existentes sólo refrescan <c>UpdatedAtUtc</c>. El chunking lo hace el caller.
    /// </summary>
    Task UpsertBulkAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> customerIds,
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
