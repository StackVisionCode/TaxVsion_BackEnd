using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Imports;
using TaxVision.Customer.Infrastructure.Persistence;

namespace TaxVision.Customer.Infrastructure.Imports;

/// <summary>
/// Purga jobs de import en estados terminales con mas de N dias (default 90) y sus filas
/// asociadas. Tambien reintenta el borrado del archivo remoto en CloudStorage por si
/// FinishAttemptAsync no llego a hacerlo (fallo de red, crash mid-flight) — defense-in-depth,
/// un archivo ya borrado simplemente responde NotFound. Corre diario a las 03:00 UTC.
/// </summary>
internal sealed class CustomerImportCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<CustomerImportCleanupHostedService> logger
) : BackgroundService
{
    private static readonly TimeSpan DailyInterval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Espera inicial corta para no competir con startup
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Customer import cleanup iteration failed");
            }

            try
            {
                await Task.Delay(DailyInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PurgeOnceAsync(CancellationToken ct)
    {
        var keepDays = config.GetValue<int?>("CustomerImport:ReportRetentionDays") ?? 90;
        var cutoff = DateTime.UtcNow.AddDays(-keepDays);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
        var cloudStorage = scope.ServiceProvider.GetRequiredService<ICustomerImportCloudStorageClient>();

        // Filas se borran por cascade desde attempts via FK. Pero por seguridad las borramos primero.
        var oldAttempts = await db
            .CustomerImportAttempts.Where(a =>
                a.CreatedAtUtc < cutoff
                && (
                    a.Status == ImportStatus.Completed
                    || a.Status == ImportStatus.Failed
                    || a.Status == ImportStatus.Canceled
                )
            )
            .Select(a => new { a.Id, a.TenantId })
            .ToListAsync(ct);

        if (oldAttempts.Count == 0)
        {
            logger.LogInformation("Cleanup: no import attempts older than {Days} days", keepDays);
            return;
        }

        foreach (var a in oldAttempts)
        {
            var deleteResult = await cloudStorage.DeleteAsync(a.TenantId, a.Id, ct);
            if (deleteResult.IsFailure)
                logger.LogDebug(
                    "Cleanup: CloudStorage delete for import {AttemptId} failed (likely already gone): {Error}",
                    a.Id,
                    deleteResult.Error.Message
                );
        }

        var attemptIds = oldAttempts.Select(a => a.Id).ToList();
        var deletedAttempts = await db
            .CustomerImportAttempts.Where(a => attemptIds.Contains(a.Id))
            .ExecuteDeleteAsync(ct);

        logger.LogInformation(
            "Cleanup: removed {Attempts} import attempts (rows cascade) older than {Days} days",
            deletedAttempts,
            keepDays
        );
    }
}
