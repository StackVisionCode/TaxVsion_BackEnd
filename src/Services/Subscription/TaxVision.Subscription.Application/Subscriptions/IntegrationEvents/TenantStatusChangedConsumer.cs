using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

/// <summary>
/// La plataforma suspendió, cerró o reactivó una oficina (Tenant). Suspendida o cerrada: se retiene su cobro
/// (base, asientos y add-ons) sin cambiar el estado comercial. Reactivada: se libera y las renovaciones
/// vencidas se cobran en la próxima pasada de los jobs.
/// </summary>
public static class TenantStatusChangedConsumer
{
    public static async Task Handle(
        TenantStatusChangedIntegrationEvent evt,
        ISubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<TenantSubscription> logger,
        CancellationToken ct
    )
    {
        if (evt.ChangedTenantId == PlatformTenant.Id)
            return;

        var subscription = await subscriptions.GetByTenantIdAsync(evt.ChangedTenantId, ct);
        if (subscription is null)
            return;

        if (!subscription.ApplyTenantStatus(evt.IsActive, evt.TenantVersion, DateTime.UtcNow))
            return;

        // El envelope viene de la oficina de plataforma: se sella la oficina para guardar en su ámbito.
        bus.TenantId = evt.ChangedTenantId.ToString();
        await unitOfWork.SaveChangesAsync(ct);
        // Sin cobro no hay módulos: los demás servicios detienen sus procesos automáticos.
        await bus.RecalculateEntitlementsSafelyAsync(evt.ChangedTenantId, logger, ct);
        logger.LogInformation(
            "Billing for tenant {TenantId} is {State} after status {Status}.",
            evt.ChangedTenantId,
            subscription.IsBillingHeld ? "held" : "released",
            evt.Status
        );
    }
}
