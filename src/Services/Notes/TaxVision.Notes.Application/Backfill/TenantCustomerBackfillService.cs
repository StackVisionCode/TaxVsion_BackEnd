using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Notes.Application.Backfill.Abstractions;
using TaxVision.Notes.Application.Customers.Abstractions;
using TaxVision.Notes.Application.Projections.Abstractions;
using TaxVision.Notes.Domain.Backfill;
using TaxVision.Notes.Domain.Projections;

namespace TaxVision.Notes.Application.Backfill;

// ---------------------------------------------------------------------------
// Fase 4B — backfill reactivo, mismo patrón que Correspondence (única referencia real en el
// monorepo, ver TaxVision.Correspondence.Application/Backfill/TenantCustomerBackfillService.cs):
// disparado como primera línea de cada consumer de evento de Customer, NUNCA como
// IHostedService al arranque. La primera vez que Notes ve un tenant es viéndolo llegar en un
// evento — no hay endpoint M2M de enumeración de tenants en todo el repo.
//
// Divergencia consciente respecto a la redacción original del plan (03_Plan_De_Fases.md §4B,
// que describe "un job" cubriendo backfill inicial + reconciliación periódica de nombres):
// aquí se separan las dos responsabilidades. Este servicio SOLO hace el backfill inicial de un
// tenant recién descubierto (una vez, marcado por TenantBackfillState). El re-llenado de
// DisplayName faltante para tenants YA backfilled corre aparte en
// CustomerDirectoryReconciliationJob (BackgroundService periódico) usando
// ListTenantIdsWithMissingNamesAsync — evita repaginar el universo completo de Customer.Api en
// cada tick.
// ---------------------------------------------------------------------------

public sealed class TenantCustomerBackfillService(
    ITenantBackfillStateRepository stateRepository,
    ICustomerDirectoryRepository directoryRepository,
    INotesCustomerClient customerClient,
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
            // Falla de red/HTTP hacia Customer.Api ya logueada en SeedAllCustomersAsync. No se
            // marca TenantBackfillState — el próximo evento de este tenant reintenta el backfill
            // completo (contrato explícito: EnsureBackfilledAsync nunca lanza).
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
                    "Customer directory backfill for tenant {TenantId} aborted — Customer.Api listing call failed on page {Page}.",
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
