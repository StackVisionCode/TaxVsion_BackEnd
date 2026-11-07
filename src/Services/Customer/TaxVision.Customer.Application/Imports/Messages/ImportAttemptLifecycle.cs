using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Application.Imports.Messages;

internal sealed record ImportStartInfo(
    Guid TenantId,
    Guid CreatedByUserId,
    DuplicateStrategy Strategy,
    ImportSourceKind SourceKind
);

internal sealed record ImportChunkAggregate(
    int Success,
    int Updated,
    int Skipped,
    int Failed,
    List<Guid> CreatedIds,
    List<Guid> UpdatedIds
);

/// <summary>
/// Estados del intento de importación. Cada paso va en su propio scope, para que el progreso se confirme aparte de
/// los chunks de clientes. El cierre y su aviso van en la misma transacción (BB-3).
/// </summary>
internal static class ImportAttemptLifecycle
{
    internal static async Task<ImportStartInfo?> StartAsync(
        IServiceScopeFactory sf,
        Guid attemptId,
        ILogger logger,
        CancellationToken ct
    )
    {
        await using var scope = sf.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICustomerImportRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var attempt = await repo.GetByIdAsync(attemptId, ct);
        if (attempt is null)
        {
            logger.LogWarning("Import attempt {AttemptId} not found; dropping message", attemptId);
            return null;
        }
        if (attempt.IsTerminal)
        {
            logger.LogInformation(
                "Import attempt {AttemptId} already terminal ({Status}); dropping",
                attemptId,
                attempt.Status
            );
            return null;
        }

        var startRes = attempt.Start();
        if (startRes.IsFailure)
        {
            logger.LogWarning("Cannot start import {AttemptId}: {Error}", attemptId, startRes.Error.Message);
            return null;
        }
        await uow.SaveChangesAsync(ct);

        return new ImportStartInfo(attempt.TenantId, attempt.CreatedByUserId, attempt.Strategy, attempt.SourceKind);
    }

    internal static async Task<List<ImportCustomerRow>> ReadRowsAsync(
        IServiceScopeFactory sf,
        Guid tenantId,
        Guid attemptId,
        ImportSourceKind sourceKind,
        CancellationToken ct
    )
    {
        await using var scope = sf.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var cloudStorage = sp.GetRequiredService<ICustomerImportCloudStorageClient>();
        var readerFactory = sp.GetRequiredService<ICustomerImportReaderFactory>();
        var reader = readerFactory.Resolve(sourceKind);

        var downloadResult = await cloudStorage.DownloadAsync(tenantId, attemptId, ct);
        if (downloadResult.IsFailure)
            throw new InvalidOperationException(
                $"Could not download the import file from CloudStorage: {downloadResult.Error.Message}"
            );

        var rows = new List<ImportCustomerRow>();
        using var fileStream = new MemoryStream(downloadResult.Value);
        await foreach (var row in reader.ReadAsync(fileStream, ct))
        {
            rows.Add(row);
        }
        return rows;
    }

    internal static int ResolveMaxRows(IServiceScopeFactory sf)
    {
        using var scope = sf.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IConfiguration>().GetValue<int?>("CustomerImport:MaxRows")
            ?? 10_000;
    }

    internal static async Task<bool> ValidateRowCountAsync(
        IServiceScopeFactory sf,
        Guid attemptId,
        int total,
        int maxRows,
        CancellationToken ct
    )
    {
        if (total == 0)
        {
            await FailAsync(sf, attemptId, "File is empty.", ct);
            return false;
        }
        if (total > maxRows)
        {
            await FailAsync(sf, attemptId, $"File has {total} rows; maximum allowed is {maxRows}.", ct);
            return false;
        }
        return true;
    }

    internal static async Task<bool> MoveToApplyingAsync(
        IServiceScopeFactory sf,
        Guid attemptId,
        int total,
        ILogger logger,
        CancellationToken ct
    )
    {
        await using var scope = sf.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICustomerImportRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var attempt = await repo.GetByIdAsync(attemptId, ct);
        if (attempt is null)
            return false;

        attempt.SetTotalRows(total);
        var moveRes = attempt.MoveToApplying();
        if (moveRes.IsFailure)
        {
            logger.LogWarning("Cannot move to Applying: {Error}", moveRes.Error.Message);
            return false;
        }
        await uow.SaveChangesAsync(ct);
        return true;
    }

    internal static async Task FinishAsync(
        IServiceScopeFactory sf,
        Guid attemptId,
        ImportStartInfo info,
        ImportChunkAggregate aggregate,
        int total,
        ILogger logger,
        CancellationToken ct
    )
    {
        await using var scope = sf.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<ITransactionalUnitRunner>();
        var cloudStorage = scope.ServiceProvider.GetRequiredService<ICustomerImportCloudStorageClient>();

        // Estado terminal y aviso en la misma transacción (BB-3): quedan los dos o ninguno.
        var closed = false;
        await runner.RunAsync(
            async (unit, token) =>
            {
                var repo = unit.Services.GetRequiredService<ICustomerImportRepository>();
                var correlation = unit.Services.GetRequiredService<ICorrelationContext>();
                var attempt = await repo.GetByIdAsync(attemptId, token);
                if (attempt is null)
                    return false;

                if (attempt.Status == ImportStatus.Canceling)
                    attempt.ConfirmCanceled();
                else
                    attempt.Complete();

                await unit.PublishAsync(
                    new CustomersBulkImportedIntegrationEvent
                    {
                        TenantId = info.TenantId,
                        CorrelationId = correlation.CorrelationId,
                        ImportJobId = attemptId,
                        CreatedByUserId = info.CreatedByUserId,
                        NotifyUserId = attempt.NotifyUserId,
                        CompletedAtUtc = attempt.CompletedAtUtc ?? DateTime.UtcNow,
                        TotalRows = total,
                        SuccessCount = aggregate.Success,
                        UpdatedCount = aggregate.Updated,
                        SkippedCount = aggregate.Skipped,
                        FailedCount = aggregate.Failed,
                        CreatedCustomerIds = aggregate.CreatedIds,
                        UpdatedCustomerIds = aggregate.UpdatedIds,
                    }
                );
                closed = true;
                return true;
            },
            ct
        );
        if (!closed)
            return;

        // Borrar el archivo en CloudStorage: el reporte por fila queda en CustomerImportRows.
        // Best-effort y después del commit: si falla (red, CloudStorage caido), el import ya quedo terminal
        // para el usuario; CustomerImportCleanupHostedService reintenta el borrado remoto mas tarde.
        var deleteResult = await cloudStorage.DeleteAsync(info.TenantId, attemptId, ct);
        if (deleteResult.IsFailure)
            logger.LogWarning(
                "Could not delete CloudStorage file for import {AttemptId}: {Error}",
                attemptId,
                deleteResult.Error.Message
            );

        logger.LogInformation(
            "Import {AttemptId} finished: total={Total} success={Success} updated={Updated} skipped={Skipped} failed={Failed}",
            attemptId,
            total,
            aggregate.Success,
            aggregate.Updated,
            aggregate.Skipped,
            aggregate.Failed
        );
    }

    internal static async Task<bool> IsCancelRequestedAsync(IServiceScopeFactory sf, Guid attemptId, CancellationToken ct)
    {
        await using var scope = sf.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICustomerImportRepository>();
        var attempt = await repo.GetByIdAsync(attemptId, ct);
        return attempt is null || attempt.IsCancelRequested;
    }

    internal static async Task FailAsync(
        IServiceScopeFactory sf,
        Guid attemptId,
        string reason,
        CancellationToken ct
    )
    {
        await using var scope = sf.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<ITransactionalUnitRunner>();

        // Fallo visible y su aviso en la misma transacción (BB-3).
        await runner.RunAsync(
            async (unit, token) =>
            {
                var repo = unit.Services.GetRequiredService<ICustomerImportRepository>();
                var correlation = unit.Services.GetRequiredService<ICorrelationContext>();
                var attempt = await repo.GetByIdAsync(attemptId, token);
                if (attempt is null)
                    return false;

                attempt.Fail(reason);
                await unit.PublishAsync(
                    new CustomerImportFailedIntegrationEvent
                    {
                        TenantId = attempt.TenantId,
                        CorrelationId = correlation.CorrelationId,
                        ImportJobId = attempt.Id,
                        CreatedByUserId = attempt.CreatedByUserId,
                        NotifyUserId = attempt.NotifyUserId,
                        Reason = attempt.FailureReason ?? reason,
                        FailedAtUtc = attempt.CompletedAtUtc ?? DateTime.UtcNow,
                    }
                );
                return true;
            },
            ct
        );
    }
}
