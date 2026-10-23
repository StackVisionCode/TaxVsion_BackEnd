using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Employees.Reconciliation;
using TaxVision.Customer.Infrastructure.Persistence;

namespace TaxVision.Customer.Infrastructure.Employees;

/// <summary>
/// Anti-entropy del directorio de empleados: al arrancar y cada intervalo, recorre el staff de Auth y crea
/// o corrige las filas. Cubre a los usuarios anteriores a la proyección (sin evento que reprocesar) y
/// cualquier evento perdido. Mismo patrón que <c>RecipientProjectionReconciliationJob</c> de Notification.
/// </summary>
public sealed class EmployeeDirectoryReconciliationJob(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    IOptions<EmployeeDirectoryReconciliationOptions> options,
    ILogger<EmployeeDirectoryReconciliationJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);
        if (!options.Value.Enabled)
        {
            logger.LogInformation("EmployeeDirectoryReconciliationJob disabled by config; not running.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, options.Value.IntervalHours));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "EmployeeDirectoryReconciliationJob iteration failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var pageSize = Math.Max(1, options.Value.PageSize);
        var created = 0;
        var updated = 0;
        for (var page = 1; !ct.IsCancellationRequested; page++)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var client = scope.ServiceProvider.GetRequiredService<IAuthStaffDirectoryClient>();
            var remote = await client.ListStaffAsync(page, pageSize, ct);
            if (remote is null)
                return;

            var result = await ApplyAsync(scope.ServiceProvider, remote, page, ct);
            created += result.Created;
            updated += result.Updated;
            if (!remote.HasMore)
                break;
        }

        if (created + updated > 0)
            logger.LogInformation(
                "Employee directory reconciled: {Created} created, {Updated} updated.",
                created,
                updated
            );
    }

    // Un alta concurrente por evento puede ganar la clave del mismo usuario: se descarta la página y la
    // siguiente pasada la reintenta.
    private async Task<EmployeeDirectoryReconciliationResult> ApplyAsync(
        IServiceProvider services,
        RemoteStaffPage remote,
        int page,
        CancellationToken ct
    )
    {
        var directory = services.GetRequiredService<ITenantEmployeeDirectoryReconciliationRepository>();
        var db = services.GetRequiredService<CustomerDbContext>();
        var result = await EmployeeDirectoryReconciliation.ApplyPageAsync(remote.Items, directory, logger, ct);
        try
        {
            await db.SaveChangesAsync(ct);
            return result;
        }
        catch (Exception ex) when (ex is ConflictException or DbUpdateException)
        {
            logger.LogWarning(ex, "Employee directory reconciliation skipped page {Page} after a conflict.", page);
            return new EmployeeDirectoryReconciliationResult(0, 0);
        }
    }
}
