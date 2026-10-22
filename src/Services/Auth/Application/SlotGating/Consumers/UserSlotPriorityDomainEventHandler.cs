using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.SlotGating.Commands;
using TaxVision.Auth.Domain.Users.Events;
using Wolverine;

namespace TaxVision.Auth.Application.SlotGating.Consumers;

public static class UserSlotPriorityDomainEventHandler
{
    public static Task Handle(UserSlotPriorityAssigned evt, IMessageBus bus) => Recompute(evt.TenantId, bus);

    public static Task Handle(UserSlotPriorityReleased evt, IMessageBus bus) => Recompute(evt.TenantId, bus);

    public static async Task Handle(UserSlotPriorityOverridden evt, IMessageBus bus, ICorrelationContext correlation) =>
        await bus.PublishAsync(
            new UserSlotPriorityOverriddenIntegrationEvent
            {
                TenantId = evt.TenantId,
                UserId = evt.UserId,
                PreviousAt = evt.PreviousAt,
                NewAt = evt.NewAt,
                ActorAdminId = evt.ActorAdminId,
                Reason = evt.Reason,
                CorrelationId = correlation.CorrelationId,
            }
        );

    private static async Task Recompute(Guid tenantId, IMessageBus bus) =>
        await bus.PublishAsync(new RecomputeTenantSeatSlotsCommand(tenantId));
}
