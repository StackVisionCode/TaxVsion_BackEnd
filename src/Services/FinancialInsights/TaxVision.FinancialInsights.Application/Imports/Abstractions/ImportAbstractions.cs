using BuildingBlocks.Common;
using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Domain.Projections;

namespace TaxVision.FinancialInsights.Application.Imports.Abstractions;

/// <summary>Lotes por tenant explícito (sin depender del filtro global).</summary>
public interface IImportBatchRepository
{
    Task<ImportBatch?> GetAsync(Guid tenantId, Guid batchId, CancellationToken ct = default);

    Task<ImportBatch?> GetForCustomerAsync(Guid tenantId, Guid customerId, Guid batchId, CancellationToken ct = default);

    Task<ImportBatch?> GetByFileAsync(Guid tenantId, Guid fileId, CancellationToken ct = default);

    Task<ImportBatch?> GetByDocumentAsync(Guid tenantId, Guid documentId, CancellationToken ct = default);

    Task<bool> AnyFileRegisteredAsync(Guid tenantId, IReadOnlyCollection<Guid> fileIds, CancellationToken ct = default);

    /// <summary>Guarda el lote nuevo. <c>false</c> si otro lote registró uno de sus archivos al mismo tiempo.</summary>
    Task<bool> TryAddAsync(ImportBatch batch, CancellationToken ct = default);

    Task<PagedResult<ImportBatchSummaryView>> ListAsync(
        ImportBatchListFilter filter,
        int page,
        int pageSize,
        CancellationToken ct = default
    );
}

/// <param name="AssignedToUserId">Con valor, solo lotes de clientes asignados a ese usuario.</param>
public sealed record ImportBatchListFilter(
    Guid TenantId,
    Guid? CustomerId,
    ImportDocumentStatus? Status,
    Guid? AssignedToUserId
);

public interface IFinancialMovementRepository
{
    Task<IReadOnlyList<FinancialMovementView>> ListByDocumentsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default
    );

    void Add(FinancialMovement movement);
}

public interface IAiReservationRepository
{
    Task<bool> ExistsForDocumentAsync(Guid tenantId, Guid documentId, CancellationToken ct = default);

    Task<AiProcessingReservation?> GetForDocumentAsync(Guid tenantId, Guid documentId, CancellationToken ct = default);
}

/// <summary>Lecturas de las proyecciones locales que deciden el acceso, sin llamadas HTTP.</summary>
public interface IFinancialAccessReader
{
    Task<TenantDirectoryProjection?> FindTenantAsync(Guid tenantId, CancellationToken ct = default);

    Task<CustomerDirectoryProjection?> FindCustomerAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    Task<bool> IsCustomerAssignedAsync(Guid tenantId, Guid customerId, Guid userId, CancellationToken ct = default);
}

public enum ImportFileState
{
    Unavailable,
    Ineligible,
    AwaitingScan,
    Ready,
}

/// <summary>Estado del archivo en CloudStorage; necesita sus metadatos, por eso es la única consulta remota.</summary>
public interface IImportFileGateway
{
    Task<ImportFileState> CheckAsync(Guid fileId, Guid customerId, CancellationToken ct = default);
}

public sealed record TenantEntitlementsSnapshot(
    string PlanCode,
    long RevisionNumber,
    IReadOnlyList<string> EnabledModules,
    int? DocumentsPerMonth
);

/// <summary>Trae el plan del tenant cuando la proyección local aún no existe. Devuelve null si no puede.</summary>
public interface ITenantEntitlementsSnapshotClient
{
    Task<TenantEntitlementsSnapshot?> FetchAsync(Guid tenantId, CancellationToken ct = default);
}
