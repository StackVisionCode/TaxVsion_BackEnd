using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Application.Backfill.Abstractions;
using TaxVision.Calendar.Application.Customers.Abstractions;
using TaxVision.Calendar.Application.Projections.Abstractions;
using TaxVision.Calendar.Domain.Backfill;
using TaxVision.Calendar.Domain.Projections;

namespace TaxVision.Calendar.Application.Backfill;

/// <summary>
/// Siembra el directorio de customers la primera vez que Task ve un tenant. Se dispara como primera
/// línea de cada consumer de evento de Customer, nunca como hosted service al arrancar: no hay
/// endpoint que enumere tenants, así que el evento es el único descubrimiento posible.
///
/// <para>
/// Sólo hace el backfill inicial, una vez por tenant. Rellenar nombres faltantes de tenants ya
/// sembrados corre aparte en <c>CustomerDirectoryReconciliationJob</c>, para no repaginar el
/// universo completo de Customer en cada tick.
/// </para>
/// </summary>
public sealed class TenantCustomerBackfillService(
    ITenantBackfillStateRepository stateRepository,
    ICustomerDirectoryRepository directoryRepository,
    ICalendarCustomerClient customerClient,
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
        {
            // No se marca el estado: el próximo evento de este tenant reintenta el backfill completo.
            return;
        }

        await stateRepository.AddAsync(TenantBackfillState.Create(tenantId), ct);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Customer directory backfill completed for tenant {TenantId}.", tenantId);
    }

    private async Task<bool> SeedAllCustomersAsync(Guid tenantId, CancellationToken ct)
    {
        var page = 1;
        while (true)
        {
            var result = await customerClient.ListActiveCustomersAsync(tenantId, page, PageSize, ct);
            if (result is null)
            {
                logger.LogWarning(
                    "Customer directory backfill for tenant {TenantId} aborted — Customer listing call failed on page {Page}.",
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
        if (await directoryRepository.ExistsAsync(tenantId, customer.Id, ct))
            return;

        var entry = CustomerDirectoryEntry.Create(
            tenantId,
            customer.Id,
            customer.DisplayName,
            customer.IsActive ? CustomerDirectoryStatus.Active : CustomerDirectoryStatus.Inactive,
            DateTime.UtcNow
        );
        await directoryRepository.AddAsync(entry, ct);
    }
}
