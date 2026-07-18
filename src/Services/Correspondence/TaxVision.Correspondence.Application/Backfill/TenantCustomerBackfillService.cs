using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Backfill;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Application.Backfill;

/// <summary>
/// Implementación de <see cref="ITenantCustomerBackfillService"/>. Disparada desde los 6
/// consumers de eventos de Customer (Application/Projections/CustomerEvents/) — es el único
/// punto donde Correspondence "descubre" que un tenant existe, dado que no hay ningún endpoint
/// de enumeración de tenants M2M en el resto del monorepo (ver
/// project_correspondence_fase2_customer_email_backfill.md). Idempotente vía
/// <see cref="ITenantBackfillStateRepository"/>: si dos eventos del mismo tenant recién
/// descubierto llegan casi al mismo tiempo, ambos pueden intentar el backfill — el peor caso es
/// una violación de unique constraint en el segundo, que Wolverine reintenta (ver
/// CorrespondenceDbContext.SaveChangesAsync → ConflictException) y en el reintento ya encuentra
/// la marca puesta. No se agrega locking distribuido: mismo criterio de "no sobre-ingenieria"
/// que el resto de esta fase.
/// </summary>
public sealed class TenantCustomerBackfillService(
    ITenantBackfillStateRepository stateRepository,
    ICustomerEmailAddressRepository emailRepository,
    ICorrespondenceCustomerClient customerClient,
    IUnitOfWork unitOfWork,
    ILogger<TenantCustomerBackfillService> logger
) : ITenantCustomerBackfillService
{
    private const int PageSize = 100;

    public async Task EnsureBackfilledAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (await stateRepository.GetByTenantIdAsync(tenantId, ct) is not null)
            return;

        var seededEverything = await SeedAllCustomersAsync(tenantId, ct);
        if (!seededEverything)
            return;

        await stateRepository.AddAsync(TenantBackfillState.Create(tenantId), ct);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Customer email backfill completed for tenant {TenantId}.", tenantId);
    }

    /// <summary>Pagina hasta agotar el listado. Devuelve false si una página falló (red/HTTP).</summary>
    private async Task<bool> SeedAllCustomersAsync(Guid tenantId, CancellationToken ct)
    {
        var page = 1;
        while (true)
        {
            var result = await customerClient.ListActiveCustomersAsync(tenantId, page, PageSize, ct);
            if (result is null)
            {
                logger.LogWarning(
                    "Customer email backfill for tenant {TenantId} could not fetch page {Page}; will retry on the next event for this tenant.",
                    tenantId,
                    page
                );
                return false;
            }

            foreach (var customer in result.Items)
                await SeedCustomerAsync(tenantId, customer, ct);

            if (!result.HasMore)
                return true;
            page++;
        }
    }

    private async Task SeedCustomerAsync(Guid tenantId, RemoteCustomerSummary customer, CancellationToken ct)
    {
        // El listado ya pide status=Active del lado de Customer.Api — este chequeo es
        // defensa en profundidad, no el filtro principal.
        if (!customer.IsActive)
            return;

        var emailResult = EmailAddress.Create(customer.PrimaryEmail);
        if (emailResult.IsFailure)
        {
            logger.LogWarning(
                "Customer {CustomerId} for tenant {TenantId} has an invalid PrimaryEmail; skipped in backfill.",
                customer.Id,
                tenantId
            );
            return;
        }

        var existing = await emailRepository.GetByCustomerIdAsync(tenantId, customer.Id, ct);
        if (existing is not null)
            return;

        var projection = CustomerEmailAddress.Create(tenantId, customer.Id, emailResult.Value);
        await emailRepository.AddAsync(projection, ct);
    }
}
