using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.Customers.Abstractions;
using TaxVision.Calendar.Application.Projections.Abstractions;
using TaxVision.Calendar.Domain.Projections;
using TaxVision.Calendar.Infrastructure.Customers;

namespace TaxVision.Calendar.Infrastructure.Jobs;

/// <summary>
/// Repagina la fuente autoritativa completa —todos los tenants, con token de PlatformTenant— y hace
/// upsert de cada customer. Cierra el hueco de filas que nunca llegaron: un evento perdido, una
/// ráfaga, o customers que ya existían antes de que naciera el servicio.
///
/// <para>
/// Idempotente porque reusa los mismos mutadores del dominio que el consumer de eventos, así que
/// correrlo N veces converge sin duplicar. Persiste por página para acotar el change-tracker.
/// </para>
/// </summary>
public sealed class TenantCustomerFullReconciliationJob(
    IServiceProvider serviceProvider,
    ILogger<TenantCustomerFullReconciliationJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        var options = serviceProvider.GetRequiredService<IOptions<CustomerClientOptions>>().Value;
        if (!options.ReconciliationEnabled)
        {
            logger.LogInformation("TenantCustomerFullReconciliationJob disabled by config; not running.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, options.ReconciliationIntervalHours));
        var pageSize = Math.Max(1, options.ReconciliationPageSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceSafeAsync(pageSize, stoppingToken);
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOnceSafeAsync(int pageSize, CancellationToken ct)
    {
        try
        {
            await RunOnceAsync(pageSize, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TenantCustomerFullReconciliationJob iteration failed.");
        }
    }

    private async Task RunOnceAsync(int pageSize, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ICalendarCustomerClient>();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomerDirectoryRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();

        using (correlation.Push(Guid.NewGuid().ToString("N")))
        {
            // Un solo instante por corrida: refleja el estado autoritativo actual, así que siempre es
            // más nuevo que cualquier evento pasado y ApplyIfNewer converge sin retroceder.
            var observedAtUtc = DateTime.UtcNow;
            var page = 1;
            var inserted = 0;
            var updated = 0;

            while (true)
            {
                var result = await client.ListAllForReconciliationAsync(page, pageSize, ct);
                if (result is null)
                {
                    logger.LogWarning(
                        "TenantCustomerFullReconciliationJob aborted on page {Page} (Customer unreachable/unauthorized).",
                        page
                    );
                    return;
                }

                foreach (var customer in result.Items)
                {
                    if (await UpsertAsync(repository, customer, observedAtUtc, ct))
                        inserted++;
                    else
                        updated++;
                }

                await unitOfWork.SaveChangesAsync(ct);

                if (!result.HasMore)
                    break;
                page++;
            }

            if (inserted > 0 || updated > 0)
                logger.LogInformation(
                    "TenantCustomerFullReconciliationJob reconciled projections: {Inserted} inserted, {Updated} refreshed.",
                    inserted,
                    updated
                );
        }
    }

    /// <summary>Devuelve <c>true</c> si insertó una fila nueva.</summary>
    private static async Task<bool> UpsertAsync(
        ICustomerDirectoryRepository repository,
        RemoteReconciliationCustomer customer,
        DateTime observedAtUtc,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByCustomerIdAsync(customer.TenantId, customer.CustomerId, ct);
        if (existing is not null)
        {
            existing.ApplyIfNewer(customer.DisplayName, customer.Status, observedAtUtc);
            return false;
        }

        var entry = CustomerDirectoryEntry.Create(
            customer.TenantId,
            customer.CustomerId,
            customer.DisplayName,
            customer.Status,
            observedAtUtc
        );
        await repository.AddAsync(entry, ct);
        return true;
    }
}
