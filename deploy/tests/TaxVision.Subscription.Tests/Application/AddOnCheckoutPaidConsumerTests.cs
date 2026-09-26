using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.AddOns.IntegrationEvents;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Renewals;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>Al confirmarse el pago del checkout, recién ahí nace el <c>TenantAddOn</c>: el consumer lo crea
/// activo, co-termina con la base, deja el cargo inicial liquidado (sin volver a cobrar) y recalcula los
/// entitlements. Idempotente en redelivery.</summary>
public sealed class AddOnCheckoutPaidConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Activates_the_add_on_and_marks_the_intent_provisioned()
    {
        var fixture = Fixture();
        var bus = new CapturingMessageBus();

        await HandleAsync(fixture, bus);

        var addOn = Assert.Single(fixture.TenantAddOns.Added);
        Assert.Equal(AddOnStatus.Active, addOn.Status);
        Assert.Equal("email.addon", addOn.AddOnCode);
        Assert.Equal(fixture.Subscription.CurrentPeriodEndUtc, addOn.CurrentPeriodEndUtc);
        Assert.Equal(AddOnPurchaseIntentStatus.Provisioned, fixture.Intent.Status);
        Assert.Equal(addOn.Id, fixture.Intent.TenantAddOnId);
        Assert.Single(bus.Published.OfType<AddOnActivatedIntegrationEvent>());
        Assert.Single(bus.Published.OfType<RecalculateEntitlementsCommand>());
    }

    // El checkout ya cobró: el período inicial queda asentado como liquidado, nunca como cobro pendiente.
    [Fact]
    public async Task Settles_the_initial_charge_without_asking_for_another_one()
    {
        var fixture = Fixture();
        var bus = new CapturingMessageBus();

        await HandleAsync(fixture, bus);

        var renewal = Assert.Single(fixture.TenantAddOns.Added[0].Renewals);
        Assert.Equal(RenewalStatus.Succeeded, renewal.Status);
        Assert.NotNull(renewal.SucceededAtUtc);
        Assert.Empty(bus.Published.OfType<AddOnRenewalDueIntegrationEvent>());
    }

    [Fact]
    public async Task Is_idempotent_on_redelivery()
    {
        var fixture = Fixture();
        var bus = new CapturingMessageBus();
        await HandleAsync(fixture, bus);

        await HandleAsync(fixture, bus);

        Assert.Single(fixture.TenantAddOns.Added);
    }

    [Fact]
    public async Task An_event_for_another_tenant_changes_nothing()
    {
        var fixture = Fixture();
        var bus = new CapturingMessageBus();

        await HandleAsync(fixture, bus, tenantId: Guid.NewGuid());

        Assert.Empty(fixture.TenantAddOns.Added);
        Assert.Equal(AddOnPurchaseIntentStatus.Pending, fixture.Intent.Status);
    }

    private sealed record Scenario(
        AddOnPurchaseIntent Intent,
        TenantSubscription Subscription,
        AddOnDefinition Definition,
        FakeAddOnPurchaseIntentRepository Intents,
        FakeTenantAddOnRepo TenantAddOns
    );

    private static Scenario Fixture()
    {
        var plan = AddOnTestCatalog.PlanWith("starter", ["documents"]);
        var definition = AddOnTestCatalog.ModuleAddOn("email.addon", "email");
        var subscription = AddOnTestCatalog.ActiveSubscription(TenantId, plan);

        var intent = AddOnPurchaseIntent
            .Create(
                TenantId,
                definition,
                quantity: 1,
                autoRenew: true,
                Money.Create(29m, "USD").Value,
                BillingCycle.Monthly,
                proratedTotalCents: 2900,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        intent.AttachCheckout(Guid.NewGuid(), "https://pay.example/xyz", DateTime.UtcNow.AddHours(24), DateTime.UtcNow);

        return new Scenario(
            intent,
            subscription,
            definition,
            new FakeAddOnPurchaseIntentRepository(intent),
            new FakeTenantAddOnRepo()
        );
    }

    private static Task HandleAsync(Scenario scenario, CapturingMessageBus bus, Guid? tenantId = null) =>
        AddOnCheckoutPaidConsumer.Handle(
            new AddOnCheckoutPaidIntegrationEvent
            {
                TenantId = tenantId ?? TenantId,
                AddOnPurchaseIntentId = scenario.Intent.Id,
                SaaSPaymentId = Guid.NewGuid(),
                AmountPaidCents = 2900,
                Currency = "USD",
                PaidAtUtc = DateTime.UtcNow,
                ProviderPaymentReference = "pi_123",
            },
            scenario.Intents,
            new FakeSubscriptionRepo(scenario.Subscription),
            new FakeAddOnDefinitionRepository(scenario.Definition),
            scenario.TenantAddOns,
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            new FakeSubscriptionAuditLogWriter(),
            new FakeSubscriptionMetrics(),
            NullLogger<TenantAddOn>.Instance,
            CancellationToken.None
        );
}
