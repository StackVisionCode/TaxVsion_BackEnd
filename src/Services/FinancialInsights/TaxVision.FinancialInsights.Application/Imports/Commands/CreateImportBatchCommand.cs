using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;
using TaxVision.FinancialInsights.Domain.Imports;

namespace TaxVision.FinancialInsights.Application.Imports.Commands;

/// <summary>El staff registra archivos ya escaneados de un cliente visible para él.</summary>
public sealed record CreateImportBatchCommand(
    Guid TenantId,
    Guid UserId,
    Guid CustomerId,
    IReadOnlyList<Guid>? FileIds,
    bool CanViewAllCustomers
);

public static class CreateImportBatchHandler
{
    public static async Task<Result<ImportBatchView>> Handle(
        CreateImportBatchCommand command,
        IImportBatchRepository batches,
        IFinancialAccessReader access,
        TenantEntitlementsResolver entitlements,
        IImportFileGateway files,
        IOptions<FinancialCustomerVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var created = ImportBatch.Create(command.TenantId, command.CustomerId, command.UserId, command.FileIds);
        if (created.IsFailure)
            return Result.Failure<ImportBatchView>(created.Error);
        var batch = created.Value;

        var tenant = await ImportAccessRules.EnsureTenantCanImportAsync(
            command.TenantId,
            access,
            entitlements,
            [FinancialEntitlements.Module],
            ct
        );
        if (tenant.IsFailure)
            return Result.Failure<ImportBatchView>(tenant.Error);

        var customer = await ImportAccessRules.FindVisibleCustomerAsync(
            command.TenantId,
            command.UserId,
            command.CustomerId,
            command.CanViewAllCustomers,
            visibility.Value.Enabled,
            requireActive: true,
            access,
            ct
        );
        if (customer is null)
            return Result.Failure<ImportBatchView>(FinancialInsightsErrors.CustomerNotFound);

        var fileIds = batch.Documents.Select(d => d.FileId).ToArray();
        var states = await ImportAccessRules.CheckFilesAsync(fileIds, command.CustomerId, false, files, ct);
        if (states.IsFailure)
            return Result.Failure<ImportBatchView>(states.Error);
        if (await batches.AnyFileRegisteredAsync(command.TenantId, fileIds, ct))
            return Result.Failure<ImportBatchView>(FinancialInsightsErrors.FileAlreadyRegistered);

        foreach (var document in batch.Documents)
        {
            var ready = batch.MarkDocumentReady(document.Id);
            if (ready.IsFailure)
                return Result.Failure<ImportBatchView>(ready.Error);
        }

        if (!await batches.TryAddAsync(batch, ct))
            return Result.Failure<ImportBatchView>(FinancialInsightsErrors.FileAlreadyRegistered);
        return Result.Success(ImportBatchView.From(batch));
    }
}
