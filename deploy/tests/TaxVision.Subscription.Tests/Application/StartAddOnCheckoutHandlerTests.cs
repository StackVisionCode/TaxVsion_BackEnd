using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.AddOns.Commands.StartAddOnCheckout;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Settings;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// La compra de un add-on por checkout hosteado: crea la intención, resuelve el prorrateo server-side y pide
/// la sesión a PaymentApp. El add-on NO nace acá — lo activa el webhook. Los guards de elegibilidad son los
/// mismos que en la compra off-session: no se pueden esquivar eligiendo este camino.
/// </summary>
public sealed class StartAddOnCheckoutHandlerTests
{
    // Corre el orquestador con los handlers reales de reserva y adjunto, como los enruta Wolverine.
    private static Task<Result<StartAddOnCheckoutResponse>> Start(
        StartAddOnCheckoutCommand command,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IModuleRepository modules,
        IAddOnDefinitionRepository definitions,
        ISubscriptionTenantSettingsRepository settings,
        ITenantAddOnRepository tenantAddOns,
        IAddOnPurchaseIntentRepository intents,
        IAddOnCheckoutPaymentClient client,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var bus = new CapturingMessageBus();
        bus.OnInvoke = async (message, token) =>
            message switch
            {
                ReserveAddOnCheckoutCommand reserve => await ReserveAddOnCheckoutHandler.Handle(
                    reserve,
                    new TaxVision.Subscription.Application.AddOns.AddOnPurchaseEligibility(
                        subscriptions,
                        plans,
                        modules,
                        definitions,
                        settings,
                        tenantAddOns
                    ),
                    intents,
                    unitOfWork,
                    token
                ),
                AttachAddOnCheckoutCommand attach => await AttachAddOnCheckoutHandler.Handle(
                    attach,
                    intents,
                    unitOfWork,
                    bus,
                    token
                ),
                _ => throw new NotSupportedException(),
            };
        return StartAddOnCheckoutHandler.Handle(command, bus, client, ct);
    }

    private static readonly Guid TenantId = Guid.NewGuid();

    // SUB-7: la intención ya está confirmada; repetir la compra reusa su clave.
    [Fact]
    public async Task A_provider_failure_keeps_the_intent_and_the_retry_reuses_its_key()
    {
        var scenario = Scenario();
        var failing = scenario with
        {
            Client = new FakeAddOnCheckoutPaymentClient(
                Result.Failure<AddOnCheckoutClientResult>(new Error("AddOn.Checkout.ProviderError", "boom"))
            ),
        };

        await HandleAsync(failing, quantity: 1);
        var retry = await HandleAsync(scenario, quantity: 1);

        Assert.True(retry.IsSuccess);
        Assert.Single(scenario.Intents.Added);
        Assert.Equal(failing.Client.LastRequest!.IdempotencyKey, scenario.Client.LastRequest!.IdempotencyKey);
    }

    [Fact]
    public async Task Creates_an_intent_and_returns_the_checkout_url_without_activating_the_add_on()
    {
        var scenario = Scenario();

        var result = await HandleAsync(scenario, quantity: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://pay.example/xyz", result.Value.CheckoutUrl);
        Assert.Single(scenario.Intents.Added);
        Assert.Equal(AddOnPurchaseIntentStatus.Pending, scenario.Intents.Added[0].Status);
        Assert.Empty(scenario.TenantAddOns.Added);
        Assert.Equal(scenario.Intents.Added[0].ProratedTotal.Cents, scenario.Client.LastRequest!.AmountCents);
        Assert.True(scenario.Client.LastRequest.AmountCents > 0);
    }

    // Guard del doble cobro: la MISMA compra devuelve la sesión viva en vez de abrir un segundo cobro.
    [Fact]
    public async Task The_same_purchase_reuses_the_open_session_instead_of_charging_twice()
    {
        var scenario = Scenario();
        var first = await HandleAsync(scenario, quantity: 1);

        var second = await HandleAsync(scenario, quantity: 1);

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.AddOnPurchaseIntentId, second.Value.AddOnPurchaseIntentId);
        Assert.Equal(1, scenario.Client.CreateCalls);
        Assert.Single(scenario.Intents.Added);
    }

    [Fact]
    public async Task A_different_purchase_of_the_same_add_on_is_rejected_while_one_is_pending()
    {
        var scenario = Scenario();
        await HandleAsync(scenario, quantity: 1);

        var second = await HandleAsync(scenario, quantity: 3);

        Assert.True(second.IsFailure);
        Assert.Equal("AddOn.CheckoutInProgress", second.Error.Code);
        Assert.Equal(1, scenario.Client.CreateCalls);
    }

    [Fact]
    public async Task An_add_on_the_plan_already_includes_never_reaches_the_payment_service()
    {
        var scenario = Scenario(planModules: ["email"]);

        var result = await HandleAsync(scenario, quantity: 1);

        Assert.True(result.IsFailure);
        Assert.Equal("AddOn.AlreadyIncludedInPlan", result.Error.Code);
        Assert.Equal(0, scenario.Client.CreateCalls);
        Assert.Empty(scenario.Intents.Added);
    }

    [Fact]
    public async Task An_add_on_already_active_never_reaches_the_payment_service()
    {
        var scenario = Scenario(alreadyOwned: true);

        var result = await HandleAsync(scenario, quantity: 1);

        Assert.True(result.IsFailure);
        Assert.Equal("AddOn.AlreadyActive", result.Error.Code);
        Assert.Equal(0, scenario.Client.CreateCalls);
    }

    private sealed record Fixture(
        TenantSubscription Subscription,
        SubscriptionPlan Plan,
        AddOnDefinition Definition,
        FakeAddOnPurchaseIntentRepository Intents,
        FakeAddOnCheckoutPaymentClient Client,
        FakeTenantAddOnRepo TenantAddOns
    );

    private static Fixture Scenario(string[]? planModules = null, bool alreadyOwned = false)
    {
        var plan = AddOnTestCatalog.PlanWith("starter", planModules ?? ["documents"]);
        var definition = AddOnTestCatalog.ModuleAddOn("module-addon-email", "email");
        var subscription = AddOnTestCatalog.ActiveSubscription(TenantId, plan);
        var owned = alreadyOwned ? new[] { AddOnTestCatalog.Owned(TenantId, definition) } : [];

        return new Fixture(
            subscription,
            plan,
            definition,
            new FakeAddOnPurchaseIntentRepository(),
            new FakeAddOnCheckoutPaymentClient(
                Result.Success(
                    new AddOnCheckoutClientResult(
                        Guid.NewGuid(),
                        "https://pay.example/xyz",
                        "cs_123",
                        DateTime.UtcNow.AddHours(24)
                    )
                )
            ),
            new FakeTenantAddOnRepo(owned)
        );
    }

    private static Task<Result<StartAddOnCheckoutResponse>> HandleAsync(Fixture fixture, int quantity) =>
        Start(
            new StartAddOnCheckoutCommand(
                TenantId,
                fixture.Definition.Code.Value,
                quantity,
                AutoRenew: true,
                "owner@acme.test",
                "https://app/ok",
                "https://app/cancel",
                "Stripe",
                "Card",
                Guid.NewGuid()
            ),
            new FakeSubscriptionRepo(fixture.Subscription),
            new FakePlanRepo(fixture.Plan),
            new FakeModuleRepo(AddOnTestCatalog.SellableModule("email")),
            new FakeAddOnDefinitionRepository(fixture.Definition),
            new NoAddOnSettingsRepo(),
            fixture.TenantAddOns,
            fixture.Intents,
            fixture.Client,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

    private sealed class NoAddOnSettingsRepo : ISubscriptionTenantSettingsRepository
    {
        public Task<SubscriptionTenantSettings?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<SubscriptionTenantSettings?>(null);

        public Task AddAsync(SubscriptionTenantSettings settings, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
