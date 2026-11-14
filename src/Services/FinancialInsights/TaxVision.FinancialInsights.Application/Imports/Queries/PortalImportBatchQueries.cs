using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;
using TaxVision.FinancialInsights.Domain.Imports;

namespace TaxVision.FinancialInsights.Application.Imports.Queries;

/// <summary>Un lote del propio cliente del portal (sin movimientos: los revisa el staff).</summary>
public sealed record GetPortalImportBatchQuery(Guid TenantId, Guid CustomerId, Guid BatchId);

public static class GetPortalImportBatchHandler
{
    public static async Task<Result<ImportBatchView>> Handle(
        GetPortalImportBatchQuery query,
        IImportBatchRepository batches,
        IFinancialAccessReader access,
        CancellationToken ct
    )
    {
        if (await access.FindCustomerAsync(query.TenantId, query.CustomerId, ct) is not { IsActive: true })
            return Result.Failure<ImportBatchView>(FinancialInsightsErrors.BatchNotFound);

        var batch = await batches.GetForCustomerAsync(query.TenantId, query.CustomerId, query.BatchId, ct);
        return batch is null
            ? Result.Failure<ImportBatchView>(FinancialInsightsErrors.BatchNotFound)
            : Result.Success(ImportBatchView.From(batch));
    }
}

/// <summary>Lotes del propio cliente del portal, más recientes primero.</summary>
public sealed record ListPortalImportBatchesQuery(Guid TenantId, Guid CustomerId, int Page, int PageSize);

public static class ListPortalImportBatchesHandler
{
    public static async Task<Result<PagedResult<ImportBatchSummaryView>>> Handle(
        ListPortalImportBatchesQuery query,
        IImportBatchRepository batches,
        IFinancialAccessReader access,
        CancellationToken ct
    )
    {
        if (await access.FindCustomerAsync(query.TenantId, query.CustomerId, ct) is not { IsActive: true })
            return Result.Failure<PagedResult<ImportBatchSummaryView>>(FinancialInsightsErrors.CustomerNotFound);

        var page = await batches.ListAsync(
            new ImportBatchListFilter(query.TenantId, query.CustomerId, null, null),
            ImportPaging.Page(query.Page),
            ImportPaging.Size(query.PageSize),
            ct
        );
        return Result.Success(page);
    }
}
