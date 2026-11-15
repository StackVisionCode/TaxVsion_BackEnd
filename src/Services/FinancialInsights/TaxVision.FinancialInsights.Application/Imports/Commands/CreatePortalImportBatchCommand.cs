using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;
using TaxVision.FinancialInsights.Domain.Imports;

namespace TaxVision.FinancialInsights.Application.Imports.Commands;

/// <summary>
/// El cliente registra archivos de su propio customer_id; los que siguen en escaneo esperan el evento de CloudStorage.
/// <paramref name="CanUploadFiles"/> sale de la proyección local de permisos (cloudstorage.file.upload).
/// </summary>
public sealed record CreatePortalImportBatchCommand(
    Guid TenantId,
    Guid UserId,
    Guid CustomerId,
    IReadOnlyList<Guid>? FileIds,
    bool CanUploadFiles
);

public static class CreatePortalImportBatchHandler
{
    public static async Task<Result<ImportBatchView>> Handle(
        CreatePortalImportBatchCommand command,
        IImportBatchRepository batches,
        IFinancialAccessReader access,
        TenantEntitlementsResolver entitlements,
        IImportFileGateway files,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var created = ImportBatch.Create(
            command.TenantId,
            command.CustomerId,
            command.UserId,
            command.FileIds,
            ImportBatchSource.Portal
        );
        if (created.IsFailure)
            return Result.Failure<ImportBatchView>(created.Error);
        var batch = created.Value;

        if (!command.CanUploadFiles)
            return Result.Failure<ImportBatchView>(FinancialInsightsErrors.PermissionDenied);
        var tenant = await ImportAccessRules.EnsureTenantCanImportAsync(
            command.TenantId,
            access,
            entitlements,
            [FinancialEntitlements.Module, FinancialEntitlements.DocumentsModule],
            ct
        );
        if (tenant.IsFailure)
            return Result.Failure<ImportBatchView>(tenant.Error);
        if (await access.FindCustomerAsync(command.TenantId, command.CustomerId, ct) is not { IsActive: true })
            return Result.Failure<ImportBatchView>(FinancialInsightsErrors.CustomerNotFound);

        var fileIds = batch.Documents.Select(d => d.FileId).ToArray();
        var states = await ImportAccessRules.CheckFilesAsync(fileIds, command.CustomerId, true, files, ct);
        if (states.IsFailure)
            return Result.Failure<ImportBatchView>(states.Error);
        if (await batches.AnyFileRegisteredAsync(command.TenantId, fileIds, ct))
            return Result.Failure<ImportBatchView>(FinancialInsightsErrors.FileAlreadyRegistered);

        foreach (var (fileId, _) in states.Value.Where(s => s.Value == ImportFileState.Ready))
            batch.MarkFileReady(fileId);
        if (!await batches.TryAddAsync(batch, ct))
            return Result.Failure<ImportBatchView>(FinancialInsightsErrors.FileAlreadyRegistered);

        // FileAvailable puede llegar antes del commit del lote: se vuelve a mirar lo pendiente.
        var changed = false;
        foreach (var (fileId, _) in states.Value.Where(s => s.Value == ImportFileState.AwaitingScan))
        {
            var current = await files.CheckAsync(fileId, command.CustomerId, ct);
            if (current == ImportFileState.Ready)
                changed |= batch.MarkFileReady(fileId).IsSuccess;
            else if (current == ImportFileState.Ineligible)
                changed |= batch.RejectFile(fileId, "FileNotEligible").IsSuccess;
        }
        if (changed)
            await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(ImportBatchView.From(batch));
    }
}
