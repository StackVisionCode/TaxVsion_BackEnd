using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Infrastructure.Customers;
using TaxVision.Correspondence.Infrastructure.Observability;
using TaxVision.Correspondence.Infrastructure.Persistence;
using Wolverine;

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

    internal async Task RunOnceSafeAsync(int pageSize, CancellationToken ct)
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
        var db = scope.ServiceProvider.GetRequiredService<CorrespondenceDbContext>();
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        using (correlation.Push(Guid.NewGuid().ToString("N")))
        {
            var page = 1;
            var inserted = 0;
            var updated = 0;

            while (true)
            {
                // Marca = hora de lectura del estado autoritativo: lo anterior que llegue después ya está reflejado.
                var readAtUtc = DateTime.UtcNow;
                var result = await client.ListPageAsync(page, pageSize, ct);
                if (result is null)
                {
                    logger.LogWarning(
                        "CustomerProjectionReconciliationJob aborted on page {Page} (Customer.Api unreachable/unauthorized).",
                        page
                    );
                    return;
                }

                // FindActiveByAddressAsync no ve lo agregado en la página todavía sin guardar.
                var activatedInPage = new HashSet<(Guid TenantId, string Email)>();
                var pageInserted = 0;
                var pageUpdated = 0;
                var links = new List<LinkUnlinkedMailCommand>();
                foreach (var customer in result.Items)
                {
                    var applied = await UpsertAsync(repository, customer, readAtUtc, activatedInPage, logger, ct);
                    if (applied == UpsertOutcome.Inserted)
                        pageInserted++;
                    else if (applied == UpsertOutcome.Updated)
                        pageUpdated++;

                    // COR-05: los importados entran por aquí, sin evento propio.
                    if (applied is UpsertOutcome.Inserted or UpsertOutcome.Updated && customer.IsActive)
                        links.Add(
                            new LinkUnlinkedMailCommand(
                                customer.TenantId,
                                customer.CustomerId,
                                EmailAddress.Create(customer.PrimaryEmail).Value.NormalizedValue
                            )
                        );
                }

                // Persistir por página para acotar el tamaño del change-tracker en catálogos grandes.
                try
                {
                    await db.SaveChangesAsync(ct);
                    inserted += pageInserted;
                    updated += pageUpdated;
                    await LinkMailAsync(bus, links, ct);
                }
                // El DbContext traduce la violación de índice único a ConflictException.
                catch (Exception ex) when (ex is DbUpdateException or ConflictException)
                {
                    // Una página mala no corta las siguientes; la próxima corrida la reintenta.
                    logger.LogError(
                        ex,
                        "CustomerProjectionReconciliationJob could not save page {Page}; skipping it.",
                        page
                    );
                    db.ChangeTracker.Clear();
                }

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
        DateTime readAtUtc,
        HashSet<(Guid TenantId, string Email)> activatedInPage,
        ILogger logger,
        CancellationToken ct
    )
    {
        if (!customer.IsActive)
            return await DeactivateAsync(repository, customer, readAtUtc, ct);

        var emailResult = EmailAddress.Create(customer.PrimaryEmail);
        if (emailResult.IsFailure)
            return UpsertOutcome.Skipped; // misma regla que el consumer: sin email válido no hay proyección.

        var email = emailResult.Value;
        var existing = await repository.GetByCustomerIdAsync(customer.TenantId, customer.CustomerId, ct);
        var activates = existing is null || !existing.IsActive || existing.EmailAddress != email.NormalizedValue;

        // IX_CustomerEmailAddresses_TenantId_EmailAddress_Active es única por email activo dentro del
        // tenant: si el email ya es de otro cliente activo, se omite (como los consumers) en vez de
        // romper el guardado de la página. A quién pertenece un email compartido lo decide el negocio.
        if (activates && await IsTakenAsync(repository, customer, email, activatedInPage, logger, ct))
            return UpsertOutcome.Skipped;

        if (existing is not null)
        {
            var changed = false;
            if (existing.EmailAddress != email.NormalizedValue)
                changed = existing.UpdateEmail(email, readAtUtc);
            if (!existing.IsActive)
                changed |= existing.Reactivate(readAtUtc);
            if (changed && existing.IsActive)
                activatedInPage.Add((customer.TenantId, existing.EmailAddress));
            return changed ? UpsertOutcome.Updated : UpsertOutcome.Unchanged;
        }

        var projection = CustomerEmailAddress.Create(customer.TenantId, customer.CustomerId, email, readAtUtc);
        await repository.AddAsync(projection, ct);
        activatedInPage.Add((customer.TenantId, email.NormalizedValue));
        return UpsertOutcome.Inserted;
    }

    // Una unidad por cliente: un correo que no se pudo enlazar no corta el resto ni la página siguiente.
    private async Task LinkMailAsync(IMessageBus bus, List<LinkUnlinkedMailCommand> links, CancellationToken ct)
    {
        foreach (var link in links)
        {
            try
            {
                await bus.InvokeAsync(link, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not link pending mail for customer {CustomerId}.", link.CustomerId);
            }
        }
    }

    private static async Task<bool> IsTakenAsync(
        ICustomerEmailAddressRepository repository,
        RemoteCustomerRecord customer,
        EmailAddress email,
        HashSet<(Guid TenantId, string Email)> activatedInPage,
        ILogger logger,
        CancellationToken ct
    )
    {
        var owner = await repository.FindActiveByAddressAsync(customer.TenantId, email.NormalizedValue, ct);
        var taken =
            activatedInPage.Contains((customer.TenantId, email.NormalizedValue))
            || (owner is not null && owner.CustomerId != customer.CustomerId);
        if (!taken)
            return false;

        CorrespondenceMetrics.ProjectionReconcileConflictsTotal.Add(
            1,
            new KeyValuePair<string, object?>("source", "projection_job")
        );
        logger.LogWarning(
            "CustomerProjectionReconciliationJob: email for customer {CustomerId} (tenant {TenantId}) is already active for a different customer {ExistingCustomerId}; skipping projection.",
            customer.CustomerId,
            customer.TenantId,
            owner?.CustomerId
        );
        return true;
    }

    private static async Task<UpsertOutcome> DeactivateAsync(
        ICustomerEmailAddressRepository repository,
        RemoteCustomerRecord customer,
        DateTime readAtUtc,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByCustomerIdAsync(customer.TenantId, customer.CustomerId, ct);
        // Igual que CustomerDeactivatedConsumer/CustomerArchivedConsumer: no-op si no existe la fila o si
        // ya está soft-deleted (SoftDelete es idempotente, pero solo cuenta como cambio la primera vez).
        if (existing is null || !existing.IsActive)
            return UpsertOutcome.Unchanged;

        return existing.SoftDelete(readAtUtc) ? UpsertOutcome.Updated : UpsertOutcome.Unchanged;
    }

    private enum UpsertOutcome
    {
        Inserted,
        Updated,
        Unchanged,
        Skipped,
    }
}
