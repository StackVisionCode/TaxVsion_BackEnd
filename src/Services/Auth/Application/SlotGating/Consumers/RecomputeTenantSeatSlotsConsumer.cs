using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.Auth.Application.SlotGating.Commands;
using Wolverine;

namespace TaxVision.Auth.Application.SlotGating.Consumers;

/// <summary>
/// No suscribe directo a <c>TenantEntitlementsChangedIntegrationEvent</c> ni a
/// <c>TenantPlanLimitsUpdatedIntegrationEvent</c>: ya los traduce indirectamente
/// <c>TenantEntitlementsChangedConsumer</c> a un <see cref="Commands.RecomputeTenantSeatSlotsCommand"/>,
/// evitando doble recompute por el mismo cambio.
/// </summary>
public sealed class RecomputeTenantSeatSlotsConsumer
{
    public static Task Handle(UserRegisteredIntegrationEvent evt, IMessageBus bus) => Recompute(evt.TenantId, bus);

    public static Task Handle(UserDeactivatedIntegrationEvent evt, IMessageBus bus) => Recompute(evt.TenantId, bus);

    public static Task Handle(UserReactivatedIntegrationEvent evt, IMessageBus bus) => Recompute(evt.TenantId, bus);

    public static Task Handle(UserOffboardedIntegrationEvent evt, IMessageBus bus) => Recompute(evt.TenantId, bus);

    public static Task Handle(UserSlotPriorityOverriddenIntegrationEvent evt, IMessageBus bus) =>
        Recompute(evt.TenantId, bus);

    public static Task Handle(SubscriptionSeatSuspendedIntegrationEvent evt, IMessageBus bus) =>
        Recompute(evt.TenantId, bus);

    public static Task Handle(SubscriptionSeatReactivatedIntegrationEvent evt, IMessageBus bus) =>
        Recompute(evt.TenantId, bus);

    private static async Task Recompute(Guid tenantId, IMessageBus bus) =>
        await bus.PublishAsync(new RecomputeTenantSeatSlotsCommand(tenantId));
}
