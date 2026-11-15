using BuildingBlocks.Common;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;
using TaxVision.FinancialInsights.Domain.Imports;

namespace TaxVision.FinancialInsights.Application.Imports.Queries;

/// <summary>Detalle de un lote con sus movimientos sugeridos, si el cliente es visible para el staff.</summary>
public sealed record GetImportBatchQuery(Guid TenantId, Guid UserId, Guid BatchId, bool CanViewAllCustomers);

public static class GetImportBatchHandler
{
    public static async Task<Result<ImportBatchDetailView>> Handle(
        GetImportBatchQuery query,
        IImportBatchRepository batches,
        IFinancialMovementRepository movements,
        IFinancialAccessReader access,
        IOptions<FinancialCustomerVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var batch = await batches.GetAsync(query.TenantId, query.BatchId, ct);
        if (batch is null)
            return Result.Failure<ImportBatchDetailView>(FinancialInsightsErrors.BatchNotFound);

        // Lectura: también el historial de un cliente desactivado.
        var customer = await ImportAccessRules.FindVisibleCustomerAsync(
            query.TenantId,
            query.UserId,
            batch.CustomerId,
            query.CanViewAllCustomers,
            visibility.Value.Enabled,
            requireActive: false,
            access,
            ct
        );
        if (customer is null)
            return Result.Failure<ImportBatchDetailView>(FinancialInsightsErrors.BatchNotFound);

        var suggestions = await movements.ListByDocumentsAsync(
            query.TenantId,
            batch.Documents.Select(d => d.Id).ToArray(),
            ct
        );
        return Result.Success(
            new ImportBatchDetailView(
                batch.Id,
                batch.CustomerId,
                customer.DisplayName,
                batch.CreatedBy,
                batch.CreatedAtUtc,
                batch.Documents.Select(ImportDocumentView.From).ToArray(),
                suggestions
            )
        );
    }
}

/// <summary>Lotes del tenant, más recientes primero, filtrados por cliente y estado de documento.</summary>
public sealed record ListImportBatchesQuery(
    Guid TenantId,
    Guid UserId,
    bool CanViewAllCustomers,
    Guid? CustomerId,
    ImportDocumentStatus? Status,
    int Page,
    int PageSize
);

public static class ListImportBatchesHandler
{
    public static Task<PagedResult<ImportBatchSummaryView>> Handle(
        ListImportBatchesQuery query,
        IImportBatchRepository batches,
        IOptions<FinancialCustomerVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAllCustomers ? query.UserId : (Guid?)null;
        return batches.ListAsync(
            new ImportBatchListFilter(query.TenantId, query.CustomerId, query.Status, assignedTo),
            ImportPaging.Page(query.Page),
            ImportPaging.Size(query.PageSize),
            ct
        );
    }
}

/// <summary>Página 1+ y tamaño 1..100 (20 por omisión).</summary>
public static class ImportPaging
{
    public const int DefaultSize = 20;
    public const int MaxSize = 100;

    public static int Page(int page) => page < 1 ? 1 : page;

    public static int Size(int size) => size is < 1 or > MaxSize ? DefaultSize : size;
}
