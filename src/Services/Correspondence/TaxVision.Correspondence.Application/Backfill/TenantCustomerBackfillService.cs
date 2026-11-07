using TaxVision.Correspondence.Application.Abstractions;
using Wolverine;

namespace TaxVision.Correspondence.Application.Backfill;

/// <summary>
/// Disparada desde los 6 consumers de eventos de Customer: es el único punto donde Correspondence descubre que un
/// tenant existe. Solo encola el backfill (COR-08); paginar dentro del consumer dejaba HTTP en su transacción. El
/// comando no depende de lo que guarde el consumer: si se revierte, repetirlo solo adelanta una página.
/// </summary>
public sealed class TenantCustomerBackfillService(ITenantBackfillStateRepository stateRepository, IMessageBus bus)
    : ITenantCustomerBackfillService
{
    public async Task EnsureBackfilledAsync(Guid tenantId, CancellationToken ct = default)
    {
        var state = await stateRepository.GetByTenantIdAsync(tenantId, ct);
        if (state is { IsCompleted: true })
            return;

        await bus.PublishAsync(new BackfillTenantCustomersCommand(tenantId));
    }
}
