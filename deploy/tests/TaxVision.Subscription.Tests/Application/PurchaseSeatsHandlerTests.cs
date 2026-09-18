using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Seats.Commands.PurchaseSeats;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Settings;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// Comprar asientos Standard (capacidad de cupo): se activan al comprar (co-terminados a la base) y se
/// publica un intent de cobro prorrateado por asiento — reusa el pipeline de renovación
/// (<see cref="SeatRenewalDueIntegrationEvent"/> → PaymentApp). Un tipo de asiento gratis ($0) se activa
/// pero no genera cobro.
/// </summary>
public sealed class PurchaseSeatsHandlerTests
{
    [Fact]
    public async Task Activates_seats_and_publishes_one_prorated_initial_charge_per_seat()
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();
        var seatRepo = new CapturingSeatRepo();
        var bus = new CapturingMessageBus();
        var unitOfWork = new FakeUnitOfWork();
        var metrics = new FakeSubscriptionMetrics();
        var audit = new FakeSubscriptionAuditLogWriter();

        var result = await PurchaseSeatsHandler.Handle(
            new PurchaseSeatsCommand(subscription.TenantId, "Standard", Quantity: 2, AutoRenew: true, Guid.NewGuid()),
            new FakeSubscriptionRepo(subscription),
            seatRepo,
            new NullSettingsRepo(),
            new FakeSeatPricingRepository(pricing),
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            audit,
            metrics,
            NullLogger<SubscriptionSeat>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        // Observabilidad: métrica de asientos comprados/facturados + una entrada de auditoría.
        Assert.Contains(("Standard", 2), metrics.SeatsPurchased);
        Assert.Single(metrics.SeatsBilled);
        Assert.Single(audit.Entries);

        // Los 2 asientos quedaron activos (capacidad de cupo), no en Available.
        Assert.Equal(2, seatRepo.Added.Count);
        Assert.All(seatRepo.Added, seat => Assert.Equal(SeatStatus.Active, seat.Status));

        var charges = bus.Published.OfType<SeatRenewalDueIntegrationEvent>().ToList();
        Assert.Equal(2, charges.Count);
        Assert.All(charges, charge => Assert.True(charge.AmountCents > 0));
        Assert.All(charges, charge => Assert.Equal("USD", charge.Currency));
        Assert.All(charges, charge => Assert.StartsWith("seat-initial-", charge.IdempotencyKey));
        Assert.Equal(2, charges.Select(charge => charge.SeatId).Distinct().Count());
        Assert.Equal(2, charges.Select(charge => charge.IdempotencyKey).Distinct().Count());

        // El cupo se recalcula tras la compra.
        Assert.Single(bus.Published.OfType<RecalculateEntitlementsCommand>());
    }

    [Fact]
    public async Task A_free_seat_type_is_activated_but_not_charged()
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();
        var seatRepo = new CapturingSeatRepo();
        var bus = new CapturingMessageBus();

        var result = await PurchaseSeatsHandler.Handle(
            new PurchaseSeatsCommand(subscription.TenantId, "Portal", Quantity: 1, AutoRenew: true, Guid.NewGuid()),
            new FakeSubscriptionRepo(subscription),
            seatRepo,
            new NullSettingsRepo(),
            new FakeSeatPricingRepository(pricing),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            new FakeSubscriptionAuditLogWriter(),
            new FakeSubscriptionMetrics(),
            NullLogger<SubscriptionSeat>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Single(seatRepo.Added);
        Assert.Equal(SeatStatus.Active, seatRepo.Added[0].Status);
        // Precio $0 → proración 0 → ningún intent de cobro, pero el asiento igual se activa.
        Assert.Empty(bus.Published.OfType<SeatRenewalDueIntegrationEvent>());
    }

    private static (TenantSubscription Subscription, SeatPricing Pricing) ActiveSubscriptionWithPricing()
    {
        var now = DateTime.UtcNow;
        var (plan, version) = CreatePublishedPlan("pro");
        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                plan,
                version,
                BillingCycle.Monthly,
                now,
                now.AddDays(30),
                Guid.Empty,
                now
            )
            .Value;

        var pricing = SeatPricing.Seed(Guid.NewGuid(), now);
        AddTier(pricing, SeatType.Standard, BillingCycle.Monthly, 15m);
        AddTier(pricing, SeatType.Standard, BillingCycle.Yearly, 150m);
        AddTier(pricing, SeatType.Portal, BillingCycle.Monthly, 0m);
        AddTier(pricing, SeatType.Portal, BillingCycle.Yearly, 0m);
        return (subscription, pricing);
    }

    private static void AddTier(SeatPricing pricing, SeatType type, BillingCycle cycle, decimal usd) =>
        pricing.AddPriceTier(SeatPriceTier.Create(pricing.Id, type, cycle, Money.Create(usd, "USD").Value).Value);

    private static (SubscriptionPlan Plan, SubscriptionPlanVersion Version) CreatePublishedPlan(string code)
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Standard, Guid.Empty, DateTime.UtcNow)
            .Value;
        var version = SubscriptionPlanVersion
            .Create(plan.Id, versionNumber: 1, trialDaysDefault: 14, [BillingCycle.Monthly, BillingCycle.Yearly])
            .Value;
        plan.AddVersion(version, Guid.Empty, DateTime.UtcNow);
        Assert.True(plan.PublishVersion(version.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow).IsSuccess);
        return (plan, version);
    }

    private sealed class NullSettingsRepo : ISubscriptionTenantSettingsRepository
    {
        public Task<SubscriptionTenantSettings?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<SubscriptionTenantSettings?>(null);

        public Task AddAsync(SubscriptionTenantSettings settings, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
