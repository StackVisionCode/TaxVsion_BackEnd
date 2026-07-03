using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;
using Sub = TaxVision.Subscription.Domain.Subscriptions.Subscription;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

public static class TenantCreatedConsumer
{
    public static async Task Handle(
        TenantCreatedIntegrationEvent evt,
        IEnrollmentRepository enrollmentRepo,
        IPlanRepository planRepo,
        ISubscriptionRepository subscriptionRepo,
        ISubscriptionModuleRepository subscriptionModuleRepo,
        IUnitOfWork uow,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        if (evt.EnrollmentId is null) return;

        var enrollment = await enrollmentRepo.GetByIdAsync(evt.EnrollmentId.Value, ct);
        if (enrollment is null) return;
        if (enrollment.Status == Domain.Enrollments.EnrollmentStatus.Provisioned) return; // idempotente

        var assignResult = enrollment.AssignTenant(evt.NewTenantId);
        if (assignResult.IsFailure) return;

        // Plan con módulos incluidos
        var plan = await planRepo.GetByIdAsync(enrollment.PlanId, ct);
        if (plan is null) return;

        var basePrice = new Money(plan.GetBasePrice(enrollment.BillingPeriod), plan.Currency);
        var seatPrice = new Money(plan.PricePerAdditionalSeat, plan.Currency);

        var activateResult = Sub.Activate(
            evt.NewTenantId,
            enrollment.Id,
            plan.Id,
            plan.Code,
            plan.Name,
            basePrice,
            seatPrice,
            enrollment.BillingPeriod,
            plan.IncludedSeats,
            DateTime.UtcNow);

        if (activateResult.IsFailure) return;

        var subscription = activateResult.Value;
        await subscriptionRepo.AddAsync(subscription, ct);

        // Crear SubscriptionModules a partir del plan
        foreach (var pm in plan.PlanModules)
        {
            var sm = SubscriptionModule.Create(subscription.Id, pm.ModuleId, isIncluded: true);
            await subscriptionModuleRepo.AddAsync(sm, ct);
        }

        await bus.ScheduleAsync(
            new SubscriptionRenewalDueIntegrationEvent
            {
                TenantId = subscription.TenantId,
                SubscriptionId = subscription.Id,
                ExpectedPeriodEnd = subscription.PeriodEndUtc,
                BillingAnchorDay = subscription.BillingAnchorDay,
                CorrelationId = correlation.CorrelationId
            },
            subscription.PeriodEndUtc);

        await bus.PublishAsync(new TenantEntitlementsChangedIntegrationEvent
        {
            TenantId = subscription.TenantId,
            SubscriptionId = subscription.Id,
            TotalAvailableSeats = subscription.TotalAvailableSeats,
            CorrelationId = correlation.CorrelationId
        });

        await uow.SaveChangesAsync(ct);
    }
}