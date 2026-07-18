using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Application.Reconciliation;

/// <summary>
/// Implementación de <see cref="ICustomerEmailReconciliationService"/>. Reusa exactamente el mismo
/// cliente M2M que <c>TenantCustomerBackfillService</c> (<see cref="ICorrespondenceCustomerClient"/>,
/// <c>GET /customers/internal/list</c>) — sin cliente/endpoint nuevo.
///
/// <para>
/// Limitación honesta (WHY, no se disfraza): <c>ListActiveCustomersAsync</c> solo devuelve
/// customers <c>IsActive=true</c> del lado de Customer — no existe hoy un listado M2M de
/// "todos, incluidos inactivos". Por eso esta reconciliación corrige de forma confiable los dos
/// casos que el plan §32 R1 realmente le preocupan ("un customer cambió su email por un camino que
/// no generó un evento limpio" y "una fila quedó soft-deleted mientras el customer ya estaba activo
/// de nuevo"), pero NO puede detectar el caso inverso (un customer se desactivó del lado de Customer
/// pero la proyección local sigue activa) sin agregar un endpoint nuevo en Customer.Api — eso es
/// scope creep de Fase 16 (que endurece lo que existe, no agrega features), así que queda fuera
/// deliberadamente.
/// </para>
/// </summary>
public sealed class CustomerEmailReconciliationService(
    ICorrespondenceCustomerClient customerClient,
    ICustomerEmailAddressRepository emailRepository,
    IUnitOfWork unitOfWork,
    ILogger<CustomerEmailReconciliationService> logger
) : ICustomerEmailReconciliationService
{
    private const int PageSize = 100;

    public async Task<CustomerEmailReconciliationResult> ReconcileTenantAsync(
        Guid tenantId,
        CancellationToken ct = default
    )
    {
        var created = 0;
        var updated = 0;
        var reactivated = 0;

        var page = 1;
        while (true)
        {
            var result = await customerClient.ListActiveCustomersAsync(tenantId, page, PageSize, ct);
            if (result is null)
            {
                logger.LogWarning(
                    "CustomerEmailReconciliation for tenant {TenantId} could not fetch page {Page}; will retry on the next run.",
                    tenantId,
                    page
                );
                await SaveIfAnyFixAsync(created, updated, reactivated, ct);
                return new CustomerEmailReconciliationResult(created, updated, reactivated, CompletedFully: false);
            }

            foreach (var customer in result.Items)
            {
                var outcome = await ReconcileCustomerAsync(tenantId, customer, ct);
                (created, updated, reactivated) = ApplyOutcome(outcome, created, updated, reactivated);
            }

            if (!result.HasMore)
                break;
            page++;
        }

        await SaveIfAnyFixAsync(created, updated, reactivated, ct);
        return new CustomerEmailReconciliationResult(created, updated, reactivated, CompletedFully: true);
    }

    private enum ReconciliationOutcome
    {
        NoChange,
        Created,
        Updated,
        Reactivated,
    }

    private async Task<ReconciliationOutcome> ReconcileCustomerAsync(
        Guid tenantId,
        RemoteCustomerSummary customer,
        CancellationToken ct
    )
    {
        var emailResult = EmailAddress.Create(customer.PrimaryEmail);
        if (emailResult.IsFailure)
        {
            logger.LogWarning(
                "Customer {CustomerId} for tenant {TenantId} has an invalid PrimaryEmail during reconciliation; skipped.",
                customer.Id,
                tenantId
            );
            return ReconciliationOutcome.NoChange;
        }

        var existing = await emailRepository.GetByCustomerIdAsync(tenantId, customer.Id, ct);
        if (existing is null)
        {
            var projection = CustomerEmailAddress.Create(tenantId, customer.Id, emailResult.Value);
            await emailRepository.AddAsync(projection, ct);
            return ReconciliationOutcome.Created;
        }

        if (!existing.IsActive)
        {
            existing.Reactivate(emailResult.Value);
            return ReconciliationOutcome.Reactivated;
        }

        if (existing.EmailAddress != emailResult.Value.NormalizedValue)
        {
            existing.UpdateEmail(emailResult.Value);
            return ReconciliationOutcome.Updated;
        }

        return ReconciliationOutcome.NoChange;
    }

    private static (int Created, int Updated, int Reactivated) ApplyOutcome(
        ReconciliationOutcome outcome,
        int created,
        int updated,
        int reactivated
    ) =>
        outcome switch
        {
            ReconciliationOutcome.Created => (created + 1, updated, reactivated),
            ReconciliationOutcome.Updated => (created, updated + 1, reactivated),
            ReconciliationOutcome.Reactivated => (created, updated, reactivated + 1),
            _ => (created, updated, reactivated),
        };

    private Task SaveIfAnyFixAsync(int created, int updated, int reactivated, CancellationToken ct) =>
        created + updated + reactivated > 0 ? unitOfWork.SaveChangesAsync(ct) : Task.CompletedTask;
}
