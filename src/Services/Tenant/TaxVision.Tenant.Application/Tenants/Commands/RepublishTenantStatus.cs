using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using Wolverine;

namespace TaxVision.Tenant.Application.Tenants.Commands;

/// <summary>PlatformAdmin: vuelve a anunciar el estado actual de una oficina para reparar las
/// proyecciones (Auth, PaymentApp, PaymentClient). Idempotente: los consumidores fijan el valor.</summary>
public sealed record RepublishTenantStatusCommand(Guid TenantId);

/// <summary>TEN-4: reposición de todas las oficinas, una por mensaje. Devuelve cuántas encoló.</summary>
public sealed record RepublishAllTenantStatusCommand;

public static class RepublishAllTenantStatusHandler
{
    private const int PageSize = 200;

    public static async Task<int> Handle(
        RepublishAllTenantStatusCommand _,
        ITenantReadService tenants,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var total = 0;
        for (var page = 1; ; page++)
        {
            var batch = await tenants.GetPageAsync(page, PageSize, ct);
            foreach (var tenant in batch)
                await bus.PublishAsync(new RepublishTenantStatusCommand(tenant.Id));

            total += batch.Count;
            if (batch.Count < PageSize)
                return total;
        }
    }
}

public static class RepublishTenantStatusHandler
{
    public static async Task<Result> Handle(
        RepublishTenantStatusCommand command,
        ITenantRepository tenants,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var tenant = await tenants.GetByIdAsync(command.TenantId, ct);
        if (tenant is null)
            return Result.Failure(new Error("Tenant.NotFound", "Tenant does not exist."));

        await bus.PublishAsync(TenantStatusEvents.From(tenant, correlation.CorrelationId));
        return Result.Success();
    }
}
