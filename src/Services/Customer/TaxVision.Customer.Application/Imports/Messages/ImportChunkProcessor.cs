using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Helpers;

namespace TaxVision.Customer.Application.Imports.Messages;

/// <summary>
/// Cada chunk en su propio scope y su propia transacción: uno malo no aborta el job, y el rollback solo afecta a ese
/// chunk. Toma el candado de alta de sus emails. Antes de cada chunk recarga el intento para ver si lo cancelaron.
/// </summary>
internal static class ImportChunkProcessor
{
    private const int ChunkSize = 500;

    internal static async Task<ImportChunkAggregate> ProcessAllAsync(
        IServiceScopeFactory sf,
        Guid attemptId,
        ImportStartInfo info,
        IReadOnlyList<ImportCustomerRow> allRows,
        ILogger logger,
        CancellationToken ct
    )
    {
        var createdIds = new List<Guid>();
        var updatedIds = new List<Guid>();
        int success = 0,
            updated = 0,
            skipped = 0,
            failed = 0;

        foreach (var chunk in Chunk(allRows, ChunkSize))
        {
            if (await ImportAttemptLifecycle.IsCancelRequestedAsync(sf, attemptId, ct))
            {
                logger.LogInformation("Import {AttemptId} canceled mid-flight; stopping.", attemptId);
                break;
            }

            var result = await ProcessChunkAsync(sf, attemptId, info, chunk, logger, ct);

            success += result.SuccessIds.Count;
            updated += result.UpdatedIds.Count;
            skipped += result.SkippedCount;
            failed += result.FailedCount;
            createdIds.AddRange(result.SuccessIds);
            updatedIds.AddRange(result.UpdatedIds);
        }

        return new ImportChunkAggregate(success, updated, skipped, failed, createdIds, updatedIds);
    }

    private sealed record ChunkResult(
        IReadOnlyList<Guid> SuccessIds,
        IReadOnlyList<Guid> UpdatedIds,
        int SkippedCount,
        int FailedCount
    );

    private static async Task<ChunkResult> ProcessChunkAsync(
        IServiceScopeFactory sf,
        Guid attemptId,
        ImportStartInfo info,
        IReadOnlyList<ImportCustomerRow> chunk,
        ILogger logger,
        CancellationToken ct
    )
    {
        await using var scope = sf.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<ITransactionalUnitRunner>();

        // Una transacción por chunk: el candado de cada email dura hasta su commit.
        var result = new ChunkResult([], [], 0, 0);
        try
        {
            await runner.RunAsync(
                async (unit, token) =>
                {
                    var processed = await ApplyChunkAsync(unit.Services, attemptId, info, chunk, logger, token);
                    if (processed is null)
                        return false;
                    result = processed;
                    return true;
                },
                ct
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Chunk save failed for import {AttemptId} (rows {From}-{To})",
                attemptId,
                chunk[0].RowNumber,
                chunk[^1].RowNumber
            );
            throw;
        }

        return result;
    }

    private static async Task<ChunkResult?> ApplyChunkAsync(
        IServiceProvider sp,
        Guid attemptId,
        ImportStartInfo info,
        IReadOnlyList<ImportCustomerRow> chunk,
        ILogger logger,
        CancellationToken ct
    )
    {
        var repo = sp.GetRequiredService<ICustomerImportRepository>();
        var detector = sp.GetRequiredService<ICustomerDuplicateDetector>();
        var creationLock = sp.GetRequiredService<ICustomerCreationLock>();

        var attempt = await repo.GetByIdAsync(attemptId, ct);
        if (attempt is null)
            return null;

        await LockChunkEmailsAsync(creationLock, info.TenantId, chunk, ct);
        var matchByRow = await BuildDuplicateMapAsync(detector, info.TenantId, chunk, ct);
        var applier = new ImportRowApplier(
            attempt,
            info,
            matchByRow,
            new ImportChunkDuplicateTracker(),
            sp.GetRequiredService<ICustomerRepository>(),
            sp.GetRequiredService<ISensitiveDataProtector>(),
            sp.GetRequiredService<ICatalogResolver>(),
            logger
        );

        var successIds = new List<Guid>();
        var updatedIds = new List<Guid>();
        var skippedCount = 0;
        var failedCount = 0;

        foreach (var raw in chunk)
        {
            var outcome = await applier.ApplyAsync(raw, ct);
            switch (outcome.Kind)
            {
                case ImportRowOutcomeKind.Success:
                    successIds.Add(outcome.CustomerId!.Value);
                    break;
                case ImportRowOutcomeKind.Updated:
                    updatedIds.Add(outcome.CustomerId!.Value);
                    break;
                case ImportRowOutcomeKind.Skipped:
                    skippedCount++;
                    break;
                case ImportRowOutcomeKind.Failed:
                    failedCount++;
                    break;
            }
        }

        return new ChunkResult(successIds, updatedIds, skippedCount, failedCount);
    }

    // Antes de la búsqueda de duplicados, que es una sola por chunk. En orden, para que dos importaciones
    // con emails en común no se bloqueen entre sí.
    private static async Task LockChunkEmailsAsync(
        ICustomerCreationLock creationLock,
        Guid tenantId,
        IReadOnlyList<ImportCustomerRow> chunk,
        CancellationToken ct
    )
    {
        var emails = chunk
            .Select(row => ImportDuplicateKeys.From(row).Email)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        foreach (var email in emails)
            await creationLock.AcquireAsync(tenantId, email, ct);
    }

    private static async Task<Dictionary<int, DuplicateMatch>> BuildDuplicateMapAsync(
        ICustomerDuplicateDetector detector,
        Guid tenantId,
        IReadOnlyList<ImportCustomerRow> chunk,
        CancellationToken ct
    )
    {
        var matches = await detector.FindDuplicatesAsync(tenantId, chunk, ct);
        return matches.ToDictionary(m => m.RowNumber);
    }

    private static IEnumerable<IReadOnlyList<ImportCustomerRow>> Chunk(
        IReadOnlyList<ImportCustomerRow> source,
        int size
    )
    {
        for (int i = 0; i < source.Count; i += size)
        {
            var len = Math.Min(size, source.Count - i);
            var arr = new ImportCustomerRow[len];
            for (int j = 0; j < len; j++)
                arr[j] = source[i + j];
            yield return arr;
        }
    }
}
