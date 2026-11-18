using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Helpers;

namespace TaxVision.Customer.Application.Imports.Messages;

/// <summary>
/// Cada chunk en su propio scope y su propia transacción: uno malo no aborta el job, y el rollback solo afecta a ese
/// chunk. Toma el candado de alta de sus emails. Antes de cada chunk recarga el intento para ver si lo cancelaron.
/// Las filas que el intento ya registró (corrida previa a un reinicio) se saltan.
/// </summary>
internal static class ImportChunkProcessor
{
    private const int ChunkSize = 500;

    internal static async Task ProcessAllAsync(
        IServiceScopeFactory sf,
        Guid attemptId,
        ImportStartInfo info,
        IReadOnlyList<ImportCustomerRow> allRows,
        ILogger logger,
        CancellationToken ct
    )
    {
        var chunkNumber = 0;
        foreach (var chunk in Chunk(allRows, ChunkSize))
        {
            chunkNumber++;
            if (await ImportAttemptLifecycle.IsCancelRequestedAsync(sf, attemptId, ct))
            {
                logger.LogInformation("Import {AttemptId} canceled mid-flight; stopping.", attemptId);
                break;
            }

            await ProcessChunkAsync(sf, attemptId, info, chunkNumber, chunk, logger, ct);
        }
    }

    private static async Task ProcessChunkAsync(
        IServiceScopeFactory sf,
        Guid attemptId,
        ImportStartInfo info,
        int chunkNumber,
        IReadOnlyList<ImportCustomerRow> chunk,
        ILogger logger,
        CancellationToken ct
    )
    {
        await using var scope = sf.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<ITransactionalUnitRunner>();

        // Una transacción por chunk: el candado de cada email dura hasta su commit.
        try
        {
            await runner.RunAsync(
                (unit, token) => ApplyChunkAsync(unit, attemptId, info, chunkNumber, chunk, logger, token),
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
    }

    private static async Task<bool> ApplyChunkAsync(
        TransactionalUnit unit,
        Guid attemptId,
        ImportStartInfo info,
        int chunkNumber,
        IReadOnlyList<ImportCustomerRow> chunk,
        ILogger logger,
        CancellationToken ct
    )
    {
        var sp = unit.Services;
        var repo = sp.GetRequiredService<ICustomerImportRepository>();

        // Candado antes de leer el intento: una corrida paralela del mismo import espera y luego ve sus filas.
        await LockChunkAsync(sp.GetRequiredService<ICustomerCreationLock>(), info.TenantId, attemptId, chunk, ct);
        var attempt = await repo.GetByIdAsync(attemptId, ct);
        if (attempt is null)
            return false;

        var pending = chunk.Where(row => !attempt.IsRowRecorded(row.RowNumber)).ToList();
        if (pending.Count == 0)
            return false;

        var matchByRow = await BuildDuplicateMapAsync(
            sp.GetRequiredService<ICustomerDuplicateDetector>(),
            info.TenantId,
            pending,
            ct
        );
        var taken = await sp.GetRequiredService<ICustomerImportIdentifierRegistry>()
            .FindTakenAsync(info.TenantId, pending, ct);
        var officeZone = await sp.GetRequiredService<IOfficeTimeZone>().ResolveAsync(info.TenantId, ct);
        var applier = new ImportRowApplier(
            attempt,
            info,
            matchByRow,
            taken,
            new ImportChunkDuplicateTracker(),
            sp.GetRequiredService<ICustomerRepository>(),
            sp.GetRequiredService<ISensitiveDataProtector>(),
            sp.GetRequiredService<ICatalogResolver>(),
            logger,
            ImportRowApplier.DefaultTaxYear(DateTime.UtcNow, officeZone)
        );

        var touched = new List<ImportRowOutcome>();
        foreach (var raw in pending)
        {
            var outcome = await applier.ApplyAsync(raw, ct);
            if (outcome.Customer is not null)
                touched.Add(outcome);
        }

        attempt.MarkProgress(DateTime.UtcNow);
        // Aviso del bloque en su misma transacción: si los clientes se guardan, el aviso también (outbox).
        if (touched.Count > 0)
            await unit.PublishAsync(
                ImportChunkAnnouncement.For(
                    attempt,
                    chunkNumber,
                    touched,
                    sp.GetRequiredService<ICorrelationContext>().CorrelationId
                )
            );
        return true;
    }

    // Antes de la búsqueda de duplicados, que es una sola por chunk. Primero el del import: dos corridas del mismo
    // import se esperan aunque las filas no traigan email. Después el de cada cliente, con la llave del alta (email,
    // o teléfono/fecha + nombre) y en orden, para que dos importaciones con clientes en común no se bloqueen.
    private static async Task LockChunkAsync(
        ICustomerCreationLock creationLock,
        Guid tenantId,
        Guid attemptId,
        IReadOnlyList<ImportCustomerRow> chunk,
        CancellationToken ct
    )
    {
        await creationLock.AcquireAsync(tenantId, $"import:{attemptId:N}", ct);

        var keys = chunk
            .Select(ImportDuplicateKeys.From)
            .Where(k =>
                k.Email is not null || (k.Name is not null && (k.PhoneE164 is not null || k.DateOfBirth is not null))
            )
            .Select(CustomerDuplicateRules.LockKey)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        foreach (var key in keys)
            await creationLock.AcquireAsync(tenantId, key, ct);
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
