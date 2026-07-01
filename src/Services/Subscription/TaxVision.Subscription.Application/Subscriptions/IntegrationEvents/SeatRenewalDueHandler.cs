using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;
using Sub = TaxVision.Subscription.Domain.Subscriptions.Subscription;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

public static class SeatRenewalDueHandler
{
    public static async Task Handle(
        SeatRenewalDueIntegrationEvent evt,
        ISubscriptionRepository repo,
        IPlanRepository planRepo,
        IUnitOfWork uow,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var subscription = await repo.GetByIdAsync(evt.SubscriptionId, ct);
        if (subscription is null) return;

        var seat = subscription.Seats.FirstOrDefault(s => s.Id == evt.SeatId);
        if (seat is null || seat.PeriodEndUtc != evt.ExpectedPeriodEnd) return; // stale check
        if (seat.Status != SeatStatus.Active) return;
        if (subscription.Status == SubscriptionStatus.Cancelled) return;

        // Leer el precio VIGENTE del plan al momento de renovar — no el precio histórico
        var plan = await planRepo.GetByIdAsync(subscription.PlanId, ct);
        if (plan is null) return;

        var currentSeatPrice = new Money(plan.PricePerAdditionalSeat, plan.Currency);
        var nextPeriodEnd = Sub.CalculateNextPeriodEnd(
            seat.PeriodEndUtc, seat.BillingPeriod, seat.BillingAnchorDay);

        await bus.PublishAsync(new SeatRenewalPaymentRequestedIntegrationEvent
        {
            TenantId = subscription.TenantId,
            SubscriptionId = subscription.Id,
            SeatId = seat.Id,
            Quantity = seat.Quantity,
            PricePerSeat = currentSeatPrice.Amount,    // ← precio vigente, no el histórico
            TotalAmount = currentSeatPrice.Amount * seat.Quantity,
            Currency = currentSeatPrice.Currency,
            BillingAnchorDay = seat.BillingAnchorDay,
            NewPeriodEnd = nextPeriodEnd,
            CorrelationId = correlation.CorrelationId
        });

        await uow.SaveChangesAsync(ct);
    }
}
