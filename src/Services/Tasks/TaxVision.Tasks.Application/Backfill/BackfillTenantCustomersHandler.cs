using TaxVision.Tasks.Application.Backfill.Abstractions;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Tasks.Application.Backfill;

/// <summary>TSK-7: pide sembrar el directorio de un tenant. Va por cola local durable y es idempotente.</summary>
public sealed record BackfillTenantCustomersCommand(Guid TenantId);

/// <summary>Pagina contra Customer sin transacción abierta: cada página se guarda por su cuenta.</summary>
public static class BackfillTenantCustomersHandler
{
    [NonTransactional]
    public static Task Handle(
        BackfillTenantCustomersCommand command,
        ITenantCustomerBackfillService backfill,
        CancellationToken ct
    ) => backfill.BackfillAsync(command.TenantId, ct);
}

/// <summary>Los consumidores solo leen el estado local y encolan; el evento se aplica igual.</summary>
public static class TenantCustomerBackfillRequest
{
    public static async Task RequestIfNeededAsync(
        Guid tenantId,
        ITenantCustomerBackfillService backfill,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (await backfill.NeedsBackfillAsync(tenantId, ct))
            await bus.PublishAsync(new BackfillTenantCustomersCommand(tenantId));
    }
}
