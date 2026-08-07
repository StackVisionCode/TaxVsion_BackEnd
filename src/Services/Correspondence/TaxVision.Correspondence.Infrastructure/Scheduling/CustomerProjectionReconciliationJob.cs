using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Infrastructure.Customers;

namespace TaxVision.Correspondence.Infrastructure.Scheduling;

/// <summary>
/// Auto-reparación de la proyección <see cref="CustomerEmailAddress"/>: re-pagina la fuente
/// autoritativa completa (todos los tenants) vía <c>GET internal/customers/reconciliation</c> y hace
/// upsert de cada customer. Cierra la deuda de raíz — antes la proyección solo se poblaba con eventos
/// en vivo (más el backfill/reconciliación por-tenant, que solo ve customers activos), así que cuando
/// se perdía un evento o llegaba en ráfaga (o el servicio nació después de que ya existían customers)
/// la proyección quedaba corta y sin forma de recuperarse. Idempotente: reusa los mismos mutadores de
/// dominio que los consumers (Created/Deactivated/Archived), así que correrlo N veces converge sin
/// duplicar.
///
/// <para>Mismo esqueleto que <see cref="Jobs.CustomerEmailReconciliationJob"/>: espera arranque del
/// host, scope propio por corrida, un fallo de una corrida no tumba el servicio. A diferencia de aquel,
/// este usa el endpoint global (un solo token de PlatformTenant, todos los tenants, incluye inactivos),
/// así que además refleja las desactivaciones/archivados perdidos como soft-delete local.</para>
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
        var repository = scope.ServiceProvider.GetRequiredService<ICustomerEmailAddressRepository>();
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
                    var applied = await UpsertAsync(repository, customer, logger, ct);
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

    /// <summary>
    /// Mismo efecto que los consumers de eventos: fila activa ⇒ upsert del email
    /// (<c>CustomerCreatedConsumer</c>), fila inactiva/archivada ⇒ soft-delete
    /// (<c>CustomerDeactivatedConsumer</c>/<c>CustomerArchivedConsumer</c>).
    /// </summary>
    private static async Task<UpsertOutcome> UpsertAsync(
        ICustomerEmailAddressRepository repository,
        RemoteCustomerRecord customer,
        ILogger logger,
        CancellationToken ct
    )
    {
        if (!customer.IsActive)
            return await DeactivateAsync(repository, customer, ct);

        var emailResult = EmailAddress.Create(customer.PrimaryEmail);
        if (emailResult.IsFailure)
            return UpsertOutcome.Skipped; // misma regla que el consumer: sin email válido no hay proyección.

        var email = emailResult.Value;
        var existing = await repository.GetByCustomerIdAsync(customer.TenantId, customer.CustomerId, ct);
        if (existing is not null)
        {
            var changed = false;
            if (existing.EmailAddress != email.NormalizedValue)
            {
                existing.UpdateEmail(email);
                changed = true;
            }
            if (!existing.IsActive)
            {
                existing.Reactivate();
                changed = true;
            }
            return changed ? UpsertOutcome.Updated : UpsertOutcome.Unchanged;
        }

        // IX_CustomerEmailAddresses_TenantId_EmailAddress_Active es única por email activo dentro del
        // tenant — dos customers distintos con el mismo email activo violarían el índice. Igual que el
        // consumer (CustomerCreatedConsumer.UpsertProjection): se registra y se omite la proyección en
        // vez de crashear (SaveChanges de la página) — resolver a quién pertenece un email compartido
        // es una decisión de negocio pendiente.
        var emailOwner = await repository.FindActiveByAddressAsync(customer.TenantId, email.NormalizedValue, ct);
        if (emailOwner is not null && emailOwner.CustomerId != customer.CustomerId)
        {
            logger.LogWarning(
                "CustomerProjectionReconciliationJob: email {Email} for customer {CustomerId} is already active for a different customer {ExistingCustomerId}; skipping projection.",
                email.NormalizedValue,
                customer.CustomerId,
                emailOwner.CustomerId
            );
            return UpsertOutcome.Skipped;
        }

        var projection = CustomerEmailAddress.Create(customer.TenantId, customer.CustomerId, email);
        await repository.AddAsync(projection, ct);
        return UpsertOutcome.Inserted;
    }

    private static async Task<UpsertOutcome> DeactivateAsync(
        ICustomerEmailAddressRepository repository,
        RemoteCustomerRecord customer,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByCustomerIdAsync(customer.TenantId, customer.CustomerId, ct);
        // Igual que CustomerDeactivatedConsumer/CustomerArchivedConsumer: no-op si no existe la fila o si
        // ya está soft-deleted (SoftDelete es idempotente, pero solo cuenta como cambio la primera vez).
        if (existing is null || !existing.IsActive)
            return UpsertOutcome.Unchanged;

        existing.SoftDelete();
        return UpsertOutcome.Updated;
    }

    private enum UpsertOutcome
    {
        Inserted,
        Updated,
        Unchanged,
        Skipped,
    }
}
