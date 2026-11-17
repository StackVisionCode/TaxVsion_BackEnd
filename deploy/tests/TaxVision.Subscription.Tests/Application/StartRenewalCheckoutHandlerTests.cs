using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.Commands.StartRenewalCheckout;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>Inicio de renovación/reactivación self-service por hosted-checkout (Fase 4): solo desde un estado
/// de lapso, resuelve el monto del ciclo server-side y pide a PaymentApp (M2M) la sesión. Espejo de
/// <c>StartSeatCheckoutHandlerTests</c>.</summary>
public sealed class StartRenewalCheckoutHandlerTests
{
    // Corre el orquestador con los handlers reales de reserva y adjunto, como los enruta Wolverine.
    private static Task<Result<StartRenewalCheckoutResponse>> Start(
        StartRenewalCheckoutCommand command,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IRenewalCheckoutIntentRepository intents,
        IRenewalCheckoutPaymentClient client,
        IUnitOfWork unitOfWork,
        CancellationToken ct,
        CapturingMessageBus? sharedBus = null
    )
    {
        var bus = sharedBus ?? new CapturingMessageBus();
        bus.OnInvoke = async (message, token) =>
            message switch
            {
                ReserveRenewalCheckoutCommand reserve => await ReserveRenewalCheckoutHandler.Handle(
                    reserve,
                    subscriptions,
                    plans,
                    intents,
                    new CapturingSeatRepo(),
                    new FakeTenantAddOnRepo(),
                    new FakeAddOnDefinitionRepository(),
                    unitOfWork,
                    token
                ),
                AttachRenewalCheckoutCommand attach => await AttachRenewalCheckoutHandler.Handle(
                    attach,
                    intents,
                    unitOfWork,
                    bus,
                    token
                ),
                _ => throw new NotSupportedException(),
            };
        return StartRenewalCheckoutHandler.Handle(command, bus, client, ct);
    }

    private static StartRenewalCheckoutCommand Command(Guid tenantId) =>
        new(tenantId, "owner@acme.test", "https://app/ok", "https://app/cancel", "Stripe", "Card", Guid.NewGuid());

    private static FakeRenewalCheckoutPaymentClient OkClient() =>
        new(
            Result.Success(
                new RenewalCheckoutClientResult(
                    Guid.NewGuid(),
                    "https://pay.example/xyz",
                    "cs_1",
                    DateTime.UtcNow.AddHours(24)
                )
            )
        );

    // Elegir PayPal, volver atrás y elegir Stripe abre un checkout de Stripe y cierra el de PayPal.
    [Fact]
    public async Task Coming_back_with_another_provider_opens_a_new_checkout_and_closes_the_old_one()
    {
        var (subscription, plan) = PastDueSubscription();
        var intents = new FakeRenewalCheckoutIntentRepository();
        var bus = new CapturingMessageBus();
        var paypal = OkClient();
        var stripe = OkClient();

        await StartWithAsync(Command(subscription.TenantId) with { Provider = "PayPal", Method = "Wallet" }, paypal);
        var second = await StartWithAsync(Command(subscription.TenantId), stripe);

        Assert.True(second.IsSuccess, second.IsFailure ? second.Error.Code : null);
        Assert.Equal(("Stripe", 1), (stripe.LastRequest!.Provider, stripe.CreateCalls));
        Assert.Equal(2, intents.Added.Count);
        Assert.Equal(SubscriptionRenewalIntentStatus.Cancelled, intents.Added[0].Status);
        Assert.Contains(
            bus.Published,
            message => message is TaxVision.Subscription.Application.Common.CancelHostedCheckoutCommand
        );
        // PaymentApp marca el pago reemplazado como cancelado, igual que al cancelar a mano.
        var cancelled = Assert.Single(
            bus.Published.OfType<BuildingBlocks.Messaging.SubscriptionIntegrationEvents.SubscriptionRenewalCheckoutCancelledIntegrationEvent>()
        );
        Assert.Equal(intents.Added[0].Id, cancelled.RenewalIntentId);
        Assert.Equal(intents.Added[0].SaaSPaymentId, cancelled.SaaSPaymentId);
        Assert.Equal("Payment method changed.", cancelled.Reason);

        Task<Result<StartRenewalCheckoutResponse>> StartWithAsync(
            StartRenewalCheckoutCommand command,
            FakeRenewalCheckoutPaymentClient client
        ) =>
            Start(
                command,
                new FakeSubscriptionRepo(subscription),
                new FakePlanRepo(plan),
                intents,
                client,
                new FakeUnitOfWork(),
                CancellationToken.None,
                bus
            );
    }

    // Volver con el mismo proveedor reusa el checkout abierto: un doble clic no abre dos.
    [Fact]
    public async Task Coming_back_with_the_same_provider_reuses_the_open_checkout()
    {
        var (subscription, plan) = PastDueSubscription();
        var intents = new FakeRenewalCheckoutIntentRepository();
        var client = OkClient();

        await StartWithAsync();
        await StartWithAsync();

        Assert.Equal(1, client.CreateCalls);
        Assert.Single(intents.Added);

        Task<Result<StartRenewalCheckoutResponse>> StartWithAsync() =>
            Start(
                Command(subscription.TenantId),
                new FakeSubscriptionRepo(subscription),
                new FakePlanRepo(plan),
                intents,
                client,
                new FakeUnitOfWork(),
                CancellationToken.None
            );
    }

    // SUB-7: la intención ya está confirmada; repetir la renovación reusa su clave.
    [Fact]
    public async Task A_provider_failure_keeps_the_intent_and_the_retry_reuses_its_key()
    {
        var (subscription, plan) = PastDueSubscription();
        var intents = new FakeRenewalCheckoutIntentRepository();
        var failing = new FakeRenewalCheckoutPaymentClient(
            Result.Failure<RenewalCheckoutClientResult>(new Error("Renewal.Checkout.ProviderError", "boom"))
        );
        var retryClient = OkClient();

        await StartWithAsync(failing);
        var retry = await StartWithAsync(retryClient);

        Assert.True(retry.IsSuccess);
        Assert.Single(intents.Added);
        Assert.Equal(failing.LastRequest!.IdempotencyKey, retryClient.LastRequest!.IdempotencyKey);

        Task<Result<StartRenewalCheckoutResponse>> StartWithAsync(FakeRenewalCheckoutPaymentClient client) =>
            Start(
                Command(subscription.TenantId),
                new FakeSubscriptionRepo(subscription),
                new FakePlanRepo(plan),
                intents,
                client,
                new FakeUnitOfWork(),
                CancellationToken.None
            );
    }

    [Fact]
    public async Task Creates_an_intent_and_returns_the_checkout_url_from_a_lapse_state()
    {
        var (subscription, plan) = PastDueSubscription();
        var intents = new FakeRenewalCheckoutIntentRepository();
        var client = OkClient();
        var unitOfWork = new FakeUnitOfWork();

        var result = await Start(
            Command(subscription.TenantId),
            new FakeSubscriptionRepo(subscription),
            new FakePlanRepo(plan),
            intents,
            client,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("https://pay.example/xyz", result.Value.CheckoutUrl);

        var intent = Assert.Single(intents.Added);
        Assert.NotNull(intent.SaaSPaymentId);
        Assert.Equal(4900, client.LastRequest!.AmountCents); // precio base mensual (49 USD)
        Assert.Equal("USD", client.LastRequest.Currency);
        Assert.StartsWith("subscription-renewal-checkout-", client.LastRequest.IdempotencyKey);
        Assert.Equal(2, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Renewal_sends_the_discount_to_paymentapp()
    {
        var (subscription, plan) = PastDueSubscription();
        var terms = SubscriptionDiscountTerms
            .Create(
                DiscountSource.Admin,
                null,
                2_000,
                null,
                null,
                DiscountDuration.Forever,
                null,
                DiscountAppliesTo.Base,
                null,
                "Retention deal",
                DateTime.UtcNow.AddMinutes(-1)
            )
            .Value;
        Assert.True(subscription.GrantDiscount(terms, false, Guid.NewGuid(), DateTime.UtcNow).IsSuccess);
        var intents = new FakeRenewalCheckoutIntentRepository();
        var client = OkClient();

        var result = await Start(
            Command(subscription.TenantId),
            new FakeSubscriptionRepo(subscription),
            new FakePlanRepo(plan),
            intents,
            client,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        // 49 USD con 20%: se firma y se cobra el neto; el descuento viaja aparte para el recibo.
        Assert.Equal(3_920, client.LastRequest!.AmountCents);
        Assert.Equal(980, client.LastRequest.DiscountAmountCents);
        Assert.Equal("Admin", client.LastRequest.DiscountSource);
        Assert.Equal(3_920, Assert.Single(intents.Added).Amount.Cents);
    }

    // D5: se renueva al precio firmado, aunque después se haya publicado otro.
    [Fact]
    public async Task The_renewal_checkout_charges_the_contracted_price_not_the_published_one()
    {
        var (subscription, plan) = PastDueSubscription();
        PlanTestData.PublishNewEdition(plan, monthlyUsd: 99m);
        var client = OkClient();

        var result = await Start(
            Command(subscription.TenantId),
            new FakeSubscriptionRepo(subscription),
            new FakePlanRepo(plan),
            new FakeRenewalCheckoutIntentRepository(),
            client,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(4900, client.LastRequest!.AmountCents);
    }

    // Guard del doble cobro, el mismo que ya tienen asientos, add-ons y el upgrade: mientras la sesión viva,
    // se devuelve esa misma URL en vez de abrir un segundo cobro.
    [Fact]
    public async Task A_second_attempt_reuses_the_open_session_instead_of_charging_twice()
    {
        var (subscription, plan) = PastDueSubscription();
        var intents = new FakeRenewalCheckoutIntentRepository();
        var client = OkClient();

        var first = await Start(
            Command(subscription.TenantId),
            new FakeSubscriptionRepo(subscription),
            new FakePlanRepo(plan),
            intents,
            client,
            new FakeUnitOfWork(),
            CancellationToken.None
        );
        var second = await Start(
            Command(subscription.TenantId),
            new FakeSubscriptionRepo(subscription),
            new FakePlanRepo(plan),
            intents,
            client,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.RenewalIntentId, second.Value.RenewalIntentId);
        Assert.Equal(first.Value.CheckoutUrl, second.Value.CheckoutUrl);
        Assert.Equal(1, client.CreateCalls);
        Assert.Single(intents.Added);
    }

    [Fact]
    public async Task An_active_subscription_can_prepay_the_next_cycle()
    {
        var (subscription, plan) = PastDueSubscription();
        subscription.RecoverFromPastDue(Guid.Empty, DateTime.UtcNow); // vuelve a Active

        var result = await Start(
            Command(subscription.TenantId),
            new FakeSubscriptionRepo(subscription),
            new FakePlanRepo(plan),
            new FakeRenewalCheckoutIntentRepository(),
            OkClient(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrEmpty(result.Value.CheckoutUrl));
    }

    [Fact]
    public async Task Surfaces_a_provider_failure()
    {
        var (subscription, plan) = PastDueSubscription();

        var result = await Start(
            Command(subscription.TenantId),
            new FakeSubscriptionRepo(subscription),
            new FakePlanRepo(plan),
            new FakeRenewalCheckoutIntentRepository(),
            new FakeRenewalCheckoutPaymentClient(
                Result.Failure<RenewalCheckoutClientResult>(
                    new Error("Subscription.RenewalCheckout.ProviderError", "boom")
                )
            ),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.RenewalCheckout.ProviderError", result.Error.Code);
    }

    // D11: si la edición agendada por una migración ya venció, la renovación se cobra con ella.
    [Fact]
    public async Task A_due_scheduled_edition_is_applied_and_charged_by_the_renewal_checkout()
    {
        var (subscription, plan) = PastDueSubscription();
        var next = SubscriptionPlanVersion
            .Create(plan.Id, versionNumber: 2, trialDaysDefault: 14, [BillingCycle.Monthly])
            .Value;
        next.AddPriceTier(
            PlanPriceTier.Create(next.Id, BillingCycle.Monthly, 1, null, Money.Create(39m, "USD").Value).Value
        );
        PlanTestData.EnsurePublishableFeature(next);
        plan.AddVersion(next, Guid.Empty, DateTime.UtcNow);
        Assert.True(plan.PublishVersion(next.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow).IsSuccess);
        var noticeAt = DateTime.UtcNow.AddDays(-40);
        Assert.True(
            subscription
                .ScheduleVersionChange(
                    plan,
                    next.Id,
                    DateTime.UtcNow.AddMinutes(-1),
                    Guid.NewGuid(),
                    "x",
                    Guid.Empty,
                    noticeAt
                )
                .IsSuccess
        );
        var client = OkClient();

        var result = await Start(
            Command(subscription.TenantId),
            new FakeSubscriptionRepo(subscription),
            new FakePlanRepo(plan),
            new FakeRenewalCheckoutIntentRepository(),
            client,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(3900, client.LastRequest!.AmountCents);
        Assert.Equal(next.Id, subscription.PlanVersionId);
        Assert.Null(subscription.ScheduledVersionId);
    }

    private static (TenantSubscription Subscription, SubscriptionPlan Plan) PastDueSubscription()
    {
        var now = DateTime.UtcNow;
        var (plan, version) = CreatePublishedPlanWithBasePrice("pro", 49m);
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
        subscription.MarkPastDueBecauseRenewalFailed("card_declined", Guid.Empty, now);
        return (subscription, plan);
    }

    private static (SubscriptionPlan Plan, SubscriptionPlanVersion Version) CreatePublishedPlanWithBasePrice(
        string code,
        decimal monthlyUsd
    )
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Starter, Guid.Empty, DateTime.UtcNow)
            .Value;
        var version = SubscriptionPlanVersion
            .Create(plan.Id, versionNumber: 1, trialDaysDefault: 14, [BillingCycle.Monthly])
            .Value;
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(monthlyUsd, "USD").Value).Value
        );
        PlanTestData.EnsurePublishableFeature(version);
        plan.AddVersion(version, Guid.Empty, DateTime.UtcNow);
        Assert.True(plan.PublishVersion(version.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow).IsSuccess);
        return (plan, version);
    }
}
