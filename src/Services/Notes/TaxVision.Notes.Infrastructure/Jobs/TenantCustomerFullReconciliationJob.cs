using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notes.Application.Customers.Abstractions;
using TaxVision.Notes.Application.Projections.Abstractions;
using TaxVision.Notes.Domain.Projections;
using TaxVision.Notes.Infrastructure.Customers;

namespace TaxVision.Notes.Infrastructure.Jobs;

/// <summary>
/// Backfill de FILAS FALTANTES de la proyección <see cref="CustomerDirectoryEntry"/> — la deuda que
/// <see cref="CustomerDirectoryReconciliationJob"/> nunca cerró (ese solo rellena DisplayName de filas
/// que YA existen; nunca inserta un customer que Notes jamás vio). Re-pagina la fuente autoritativa
/// completa (todos los tenants) vía <c>GET customers/internal/reconciliation</c> con token de
/// PlatformTenant y hace upsert de cada customer. Cierra el hueco cuando se pierde un evento, llega en
/// ráfaga, o el servicio nació después de que ya existían customers.
///
/// <para>Idempotente: reusa los MISMOS mutadores/factory de dominio que el consumer de eventos
/// (<c>CustomerCreatedConsumer</c>: <see cref="ICustomerDirectoryRepository.GetByCustomerIdAsync"/> +
/// <see cref="CustomerDirectoryEntry.ApplyIfNewer"/> / <see cref="CustomerDirectoryEntry.Create"/> +
/// <see cref="ICustomerDirectoryRepository.AddAsync"/>), así que correrlo N veces converge sin duplicar.
/// Mismo esqueleto que <c>CustomerProjectionReconciliationJob</c> (Signature): espera arranque del host,
/// scope propio por corrida, persiste por página, un fallo de una corrida no tumba el servicio.</para>
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
        var client = scope.ServiceProvider.GetRequiredService<INotesCustomerClient>();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomerDirectoryRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();

        using (correlation.Push(Guid.NewGuid().ToString("N")))
        {
            // Snapshot único por corrida: refleja el estado autoritativo ACTUAL, así que siempre es
            // "más nuevo" que cualquier evento pasado — ApplyIfNewer converge sin retroceder.
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
                        "TenantCustomerFullReconciliationJob aborted on page {Page} (Customer.Api unreachable/unauthorized).",
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

                // Persistir por página para acotar el tamaño del change-tracker en catálogos grandes.
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

    /// <summary>Devuelve <c>true</c> si insertó una fila nueva (customer que Notes nunca había visto).</summary>
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
