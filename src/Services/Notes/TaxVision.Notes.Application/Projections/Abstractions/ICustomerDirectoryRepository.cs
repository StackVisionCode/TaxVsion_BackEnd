using TaxVision.Notes.Domain.Projections;

namespace TaxVision.Notes.Application.Projections.Abstractions;

/// <summary>
/// Proyección local de customers del tenant (Fase 4B). Consultada por Fase 5 (Application) para
/// la validación SOFT de <c>CreateNoteCommand</c> cuando <c>TargetType==Customer</c>.
/// </summary>
public interface ICustomerDirectoryRepository
{
    Task<bool> ExistsAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    Task<string?> GetDisplayNameAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    Task<CustomerDirectoryEntry?> GetByCustomerIdAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    Task AddAsync(CustomerDirectoryEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Upsert set-based por <c>MERGE</c> (raw SQL) para <c>CustomersBulkImportedIntegrationEvent</c> —
    /// nunca carga N entidades en memoria ni hace fetch por id. Filas nuevas nacen con
    /// <c>DisplayName = null</c> (el evento no trae PII); filas existentes solo actualizan
    /// <c>UpdatedAtUtc</c>. Chunking (~500) es responsabilidad del caller.
    /// </summary>
    Task UpsertBulkAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> customerIds,
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
