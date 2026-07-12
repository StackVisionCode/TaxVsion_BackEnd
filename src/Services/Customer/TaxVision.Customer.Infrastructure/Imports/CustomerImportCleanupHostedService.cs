using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Domain.Imports;
using TaxVision.Customer.Infrastructure.Persistence;

namespace TaxVision.Customer.Infrastructure.Imports;

/// <summary>
/// Purga jobs de import en estados terminales con mas de N dias (default 90) y sus filas asociadas.
/// Tambien borra cualquier CustomerImportFile huerfano (no deberia haber, pero defense-in-depth).
/// Corre diario a las 03:00 UTC.
///
/// TODO: cuando exista CloudStorage Service, ajustar para borrar tambien el blob remoto del fileId.
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

        // Filas se borran por cascade desde attempts via FK. Pero por seguridad las borramos primero.
        var oldAttemptIds = await db
            .CustomerImportAttempts.Where(a =>
                a.CreatedAtUtc < cutoff
                && (
                    a.Status == ImportStatus.Completed
                    || a.Status == ImportStatus.Failed
                    || a.Status == ImportStatus.Canceled
                )
            )
            .Select(a => a.Id)
            .ToListAsync(ct);

        if (oldAttemptIds.Count == 0)
        {
            logger.LogInformation("Cleanup: no import attempts older than {Days} days", keepDays);
            return;
        }

        var deletedFiles = await db.Set<CustomerImportFile>()
            .Where(f => oldAttemptIds.Contains(f.ImportAttemptId))
            .ExecuteDeleteAsync(ct);

        var deletedAttempts = await db
            .CustomerImportAttempts.Where(a => oldAttemptIds.Contains(a.Id))
            .ExecuteDeleteAsync(ct);

        logger.LogInformation(
            "Cleanup: removed {Attempts} import attempts (rows cascade) and {Files} leftover files older than {Days} days",
            deletedAttempts,
            deletedFiles,
            keepDays
        );
    }
}
