using TaxVision.Correspondence.Application.Abstractions;
using Wolverine;

namespace TaxVision.Correspondence.Application.Backfill;

/// <summary>
/// Disparada desde los 6 consumers de eventos de Customer: es el único punto donde Correspondence descubre que un
/// tenant existe. Solo encola el backfill (COR-08); paginar dentro del consumer dejaba HTTP en su transacción. El bus
/// llega del handler: por constructor sería otro contexto y el comando saldría aunque el consumer se revierta.
/// </summary>
public sealed class TenantCustomerBackfillService(ITenantBackfillStateRepository stateRepository)
    : ITenantCustomerBackfillService
{
    public async Task EnsureBackfilledAsync(Guid tenantId, IMessageBus bus, CancellationToken ct = default)
    {
        var state = await stateRepository.GetByTenantIdAsync(tenantId, ct);
        if (state is { IsCompleted: true })
            return;

        await bus.PublishAsync(new BackfillTenantCustomersCommand(tenantId));
    }
}
