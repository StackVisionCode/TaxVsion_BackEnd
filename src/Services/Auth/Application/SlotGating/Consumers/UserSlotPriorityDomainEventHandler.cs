using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.SlotGating.Commands;
using TaxVision.Auth.Domain.Users.Events;

namespace TaxVision.Auth.Application.SlotGating.Consumers;

/// <summary>Eventos de dominio de los asientos: corren después del commit, desde la cola domain-events.</summary>
public static class UserSlotPriorityDomainEventHandler
{
    public static RecomputeTenantSeatSlotsCommand Handle(UserSlotPriorityAssigned evt) => new(evt.TenantId);

    public static RecomputeTenantSeatSlotsCommand Handle(UserSlotPriorityReleased evt) => new(evt.TenantId);

    public static UserSlotPriorityOverriddenIntegrationEvent Handle(
        UserSlotPriorityOverridden evt,
        ICorrelationContext correlation
    ) =>
        new()
        {
            TenantId = evt.TenantId,
            UserId = evt.UserId,
            PreviousAt = evt.PreviousAt,
            NewAt = evt.NewAt,
            ActorAdminId = evt.ActorAdminId,
            Reason = evt.Reason,
            CorrelationId = correlation.CorrelationId,
        };
}
