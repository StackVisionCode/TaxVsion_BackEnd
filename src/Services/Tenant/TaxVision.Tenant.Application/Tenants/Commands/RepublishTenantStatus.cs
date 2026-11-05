using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using Wolverine;

namespace TaxVision.Tenant.Application.Tenants.Commands;

/// <summary>PlatformAdmin: vuelve a anunciar el estado actual de una oficina para reparar las
/// proyecciones (Auth, PaymentApp, PaymentClient). Idempotente: los consumidores fijan el valor.</summary>
public sealed record RepublishTenantStatusCommand(Guid TenantId);

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
