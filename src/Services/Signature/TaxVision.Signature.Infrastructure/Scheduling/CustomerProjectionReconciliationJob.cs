using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Infrastructure.Sealing.HttpClients;

namespace TaxVision.Signature.Infrastructure.Scheduling;

/// <summary>
/// Auto-reparación de la proyección <see cref="CustomerEmailProjection"/>: re-pagina la fuente
/// autoritativa completa (todos los tenants) vía <c>GET internal/customers/reconciliation</c> y hace
/// upsert de cada customer. Cierra la deuda de raíz — antes la proyección solo se poblaba con eventos
/// en vivo, así que cuando se perdía un evento o llegaba en ráfaga (o el servicio nació después de que
/// ya existían customers) la proyección quedaba corta y sin forma de recuperarse. Idempotente: reusa el
/// mismo factory/mutadores de dominio que el consumer, así que correrlo N veces converge sin duplicar.
///
/// <para>Mismo esqueleto que <see cref="ExpirationScheduler"/>: espera arranque del host, scope propio
/// por corrida, un fallo de una corrida no tumba el servicio.</para>
/// </summary>
public sealed class CustomerProjectionReconciliationJob(
    IServiceProvider serviceProvider,
    ILogger<CustomerProjectionReconciliationJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        var options = serviceProvider.GetRequiredService<IOptions<CustomerClientOptions>>().Value;
        if (!options.ReconciliationEnabled)
        {
            logger.LogInformation("CustomerProjectionReconciliationJob disabled by config; not running.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, options.ReconciliationIntervalHours));

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceSafeAsync(options.ReconciliationPageSize, stoppingToken);
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
            logger.LogError(ex, "CustomerProjectionReconciliationJob iteration failed.");
        }
    }

    private async Task RunOnceAsync(int pageSize, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ICustomerReconciliationClient>();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomerEmailProjectionRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();

        using (correlation.Push(Guid.NewGuid().ToString("N")))
        {
            var page = 1;
            var inserted = 0;
            var updated = 0;

            while (true)
            {
                var result = await client.ListPageAsync(page, pageSize, ct);
                if (result is null)
                {
                    logger.LogWarning(
                        "CustomerProjectionReconciliationJob aborted on page {Page} (Customer.Api unreachable/unauthorized).",
                        page
                    );
                    return;
                }

                foreach (var customer in result.Items)
                {
                    var applied = await UpsertAsync(repository, customer, ct);
                    if (applied == UpsertOutcome.Inserted)
                        inserted++;
                    else if (applied == UpsertOutcome.Updated)
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
                    "CustomerProjectionReconciliationJob reconciled projections: {Inserted} inserted, {Updated} updated.",
                    inserted,
                    updated
                );
        }
    }

    private static async Task<UpsertOutcome> UpsertAsync(
        ICustomerEmailProjectionRepository repository,
        RemoteCustomerRecord customer,
        CancellationToken ct
    )
    {
        var normalizedEmail = NormalizeEmail(customer.PrimaryEmail);
        if (string.IsNullOrEmpty(normalizedEmail))
            return UpsertOutcome.Skipped; // misma regla que el consumer: sin email no hay proyección.

        var existing = await repository.GetByCustomerIdAsync(customer.TenantId, customer.CustomerId, ct);
        if (existing is null)
        {
            var projection = CustomerEmailProjection.ForNewCustomer(
                customer.TenantId,
                customer.CustomerId,
                normalizedEmail,
                customer.DisplayName
            );
            if (!customer.IsActive)
                projection.MarkArchived();
            await repository.AddAsync(projection, ct);
            return UpsertOutcome.Inserted;
        }

        var changed = false;
        if (existing.NormalizedEmail != normalizedEmail)
        {
            existing.ChangeEmail(normalizedEmail);
            changed = true;
        }
        if (existing.DisplayName != customer.DisplayName)
        {
            existing.UpdateDisplayName(customer.DisplayName);
            changed = true;
        }
        if (customer.IsActive && existing.IsArchived)
        {
            existing.MarkReactivated();
            changed = true;
        }
        else if (!customer.IsActive && !existing.IsArchived)
        {
            existing.MarkArchived();
            changed = true;
        }

        return changed ? UpsertOutcome.Updated : UpsertOutcome.Unchanged;
    }

    private static string NormalizeEmail(string email) =>
        string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();

    private enum UpsertOutcome
    {
        Inserted,
        Updated,
        Unchanged,
        Skipped,
    }
}
