using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Seats.Commands.StartSeatCheckout;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>El inicio de compra por hosted-checkout (tenant sin método): crea la intención, resuelve el total
/// server-side y pide a PaymentApp (M2M) la sesión, devolviendo la URL. Un tipo gratis no puede ir por acá.</summary>
public sealed class StartSeatCheckoutHandlerTests
{
    // Corre el orquestador con los handlers reales de reserva y adjunto, como los enruta Wolverine.
    private static Task<Result<StartSeatCheckoutResponse>> Start(
        StartSeatCheckoutCommand command,
        ISubscriptionRepository subscriptions,
        ISeatPricingRepository pricing,
        ISeatPurchaseIntentRepository intents,
        ISeatCheckoutPaymentClient client,
        IUnitOfWork unitOfWork,
        CancellationToken ct,
        CapturingMessageBus? sharedBus = null
    )
    {
        var bus = sharedBus ?? new CapturingMessageBus();
        bus.OnInvoke = async (message, token) =>
            message switch
            {
                ReserveSeatCheckoutCommand reserve => await ReserveSeatCheckoutHandler.Handle(
                    reserve,
                    subscriptions,
                    pricing,
                    intents,
                    unitOfWork,
                    token
                ),
                AttachSeatCheckoutCommand attach => await AttachSeatCheckoutHandler.Handle(
                    attach,
                    intents,
                    unitOfWork,
                    bus,
                    token
                ),
                _ => throw new NotSupportedException(),
            };
        return StartSeatCheckoutHandler.Handle(command, bus, client, ct);
    }

    // SUB-7: la intención ya está confirmada; repetir la compra reusa su clave.
    [Fact]
    public async Task A_provider_failure_keeps_the_intent_and_the_retry_reuses_its_key()
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();
        var intents = new FakeSeatPurchaseIntentRepository();
        var command = Command(subscription.TenantId, "Standard", 2);
        var failing = new FakeSeatCheckoutPaymentClient(
            Result.Failure<SeatCheckoutClientResult>(new Error("Seats.Checkout.ProviderError", "boom"))
        );
        await HandleAsync(command, subscription, pricing, intents, failing);

        var retryClient = Client();
        var retry = await HandleAsync(command, subscription, pricing, intents, retryClient);

        Assert.True(retry.IsSuccess);
        Assert.Single(intents.Added);
        Assert.Equal(failing.LastRequest!.IdempotencyKey, retryClient.LastRequest!.IdempotencyKey);
    }

    // El reintento tras un fallo del proveedor no pierde el descuento: el recibo lo necesita.
    [Fact]
    public async Task The_retry_of_a_failed_checkout_keeps_its_discount_and_label()
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();
        var terms = SubscriptionDiscountTerms
            .Create(
                DiscountSource.Admin,
                null,
                2_000,
                null,
                null,
                DiscountDuration.Forever,
                null,
                DiscountAppliesTo.BaseSeatsAddOns,
                null,
                "Retention deal",
                DateTime.UtcNow.AddMinutes(-1)
            )
            .Value;
        Assert.True(subscription.GrantDiscount(terms, false, Guid.NewGuid(), DateTime.UtcNow).IsSuccess);
        var intents = new FakeSeatPurchaseIntentRepository();
        var command = Command(subscription.TenantId, "Standard", 2);
        var failing = new FakeSeatCheckoutPaymentClient(
            Result.Failure<SeatCheckoutClientResult>(new Error("Seats.Checkout.ProviderError", "boom"))
        );
        await HandleAsync(command, subscription, pricing, intents, failing);

        var retryClient = Client();
        await HandleAsync(command, subscription, pricing, intents, retryClient);

        var retried = retryClient.LastRequest!;
        Assert.Equal(failing.LastRequest!.DiscountAmountCents, retried.DiscountAmountCents);
        Assert.Equal("Admin", retried.DiscountSource);
        Assert.Equal("Discount · 20% off", retried.DiscountLabel);
    }

    [Fact]
    public async Task An_unattached_intent_of_another_purchase_is_not_reused()
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();
        var intents = new FakeSeatPurchaseIntentRepository();
        var failing = new FakeSeatCheckoutPaymentClient(
            Result.Failure<SeatCheckoutClientResult>(new Error("Seats.Checkout.ProviderError", "boom"))
        );
        await HandleAsync(Command(subscription.TenantId, "Standard", 2), subscription, pricing, intents, failing);

        var retry = await HandleAsync(
            Command(subscription.TenantId, "Standard", 5),
            subscription,
            pricing,
            intents,
            Client()
        );

        Assert.True(retry.IsSuccess);
        Assert.Equal(2, intents.Added.Count);
    }

    [Fact]
    public async Task Coming_back_with_another_provider_opens_a_new_seat_checkout_and_closes_the_old_one()
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();
        var intents = new FakeSeatPurchaseIntentRepository();
        var bus = new CapturingMessageBus();

        await StartWithAsync(
            Command(subscription.TenantId, "Standard", 2) with
            {
                Provider = "PayPal",
                Method = "Wallet",
            }
        );
        var second = await StartWithAsync(Command(subscription.TenantId, "Standard", 2));

        Assert.True(second.IsSuccess, second.IsFailure ? second.Error.Code : null);
        Assert.Equal(2, intents.Added.Count);
        Assert.Equal(SeatPurchaseIntentStatus.Cancelled, intents.Added[0].Status);
        Assert.Contains(
            bus.Published,
            message => message is TaxVision.Subscription.Application.Common.CancelHostedCheckoutCommand
        );
        // PaymentApp marca el pago reemplazado como cancelado, igual que al cancelar a mano.
        var cancelled = Assert.Single(
            bus.Published.OfType<BuildingBlocks.Messaging.SubscriptionIntegrationEvents.SeatCheckoutCancelledIntegrationEvent>()
        );
        Assert.Equal(intents.Added[0].Id, cancelled.SeatPurchaseIntentId);
        Assert.Equal(intents.Added[0].SaaSPaymentId, cancelled.SaaSPaymentId);
        Assert.Equal("Payment method changed.", cancelled.Reason);

        Task<Result<StartSeatCheckoutResponse>> StartWithAsync(StartSeatCheckoutCommand command) =>
            Start(
                command,
                new FakeSubscriptionRepo(subscription),
                new FakeSeatPricingRepository(pricing),
                intents,
                new FakeSeatCheckoutPaymentClient(
                    Result.Success(
                        new SeatCheckoutClientResult(
                            Guid.NewGuid(),
                            "https://pay/x",
                            "cs_1",
                            DateTime.UtcNow.AddHours(24)
                        )
                    )
                ),
                new FakeUnitOfWork(),
                CancellationToken.None,
                bus
            );
    }

    private static StartSeatCheckoutCommand Command(Guid tenantId, string seatType, int quantity) =>
        new(
            tenantId,
            seatType,
            quantity,
            AutoRenew: true,
            "owner@acme.test",
            "https://app/ok",
            "https://app/cancel",
            "Stripe",
            "Card",
            Guid.NewGuid()
        );

    [Fact]
    public async Task Creates_an_intent_and_returns_the_checkout_url()
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();
        var intents = new FakeSeatPurchaseIntentRepository();
        var client = new FakeSeatCheckoutPaymentClient(
            Result.Success(
                new SeatCheckoutClientResult(
                    Guid.NewGuid(),
                    "https://pay.example/xyz",
                    "cs_123",
                    DateTime.UtcNow.AddHours(24)
                )
            )
        );
        var unitOfWork = new FakeUnitOfWork();

        var result = await Start(
            Command(subscription.TenantId, "Standard", 2),
            new FakeSubscriptionRepo(subscription),
            new FakeSeatPricingRepository(pricing),
            intents,
            client,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("https://pay.example/xyz", result.Value.CheckoutUrl);

        var intent = Assert.Single(intents.Added);
        Assert.NotNull(intent.SaaSPaymentId);
        Assert.Equal("https://pay.example/xyz", intent.CheckoutUrl);
        Assert.NotNull(client.LastRequest);
        Assert.Equal(intent.ProratedTotal.Cents, client.LastRequest!.AmountCents);
        Assert.Equal("USD", client.LastRequest.Currency);
        Assert.StartsWith("seat-checkout-", client.LastRequest.IdempotencyKey);
        Assert.Equal(2, unitOfWork.SaveChangesCallCount); // guarda la intención, y de nuevo tras adjuntar el checkout
    }

    [Fact]
    public async Task A_free_seat_type_cannot_use_checkout()
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();

        var result = await Start(
            Command(subscription.TenantId, "Portal", 1),
            new FakeSubscriptionRepo(subscription),
            new FakeSeatPricingRepository(pricing),
            new FakeSeatPurchaseIntentRepository(),
            new FakeSeatCheckoutPaymentClient(
                Result.Success(new SeatCheckoutClientResult(Guid.NewGuid(), "x", "y", DateTime.UtcNow))
            ),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Seats.Checkout.NothingToCharge", result.Error.Code);
    }

    [Fact]
    public async Task Surfaces_a_provider_failure()
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();

        var result = await Start(
            Command(subscription.TenantId, "Standard", 1),
            new FakeSubscriptionRepo(subscription),
            new FakeSeatPricingRepository(pricing),
            new FakeSeatPurchaseIntentRepository(),
            new FakeSeatCheckoutPaymentClient(
                Result.Failure<SeatCheckoutClientResult>(new Error("Seats.Checkout.ProviderError", "boom"))
            ),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Seats.Checkout.ProviderError", result.Error.Code);
    }

    // Guard del doble cobro (aceptación de la F6): repetir la MISMA compra devuelve la sesión que ya existe.
    [Fact]
    public async Task Repeating_the_same_purchase_reuses_the_open_checkout_instead_of_charging_twice()
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();
        var intents = new FakeSeatPurchaseIntentRepository();
        var client = Client();
        var command = Command(subscription.TenantId, "Standard", 2);

        var first = await HandleAsync(command, subscription, pricing, intents, client);
        var second = await HandleAsync(command, subscription, pricing, intents, client);

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.SeatPurchaseIntentId, second.Value.SeatPurchaseIntentId);
        Assert.Equal(first.Value.CheckoutUrl, second.Value.CheckoutUrl);
        Assert.Single(intents.Added);
        Assert.Equal(1, client.CreateCalls);
    }

    [Fact]
    public async Task A_different_purchase_while_one_is_open_is_rejected_instead_of_opening_a_second_one()
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();
        var intents = new FakeSeatPurchaseIntentRepository();
        var client = Client();
        await HandleAsync(Command(subscription.TenantId, "Standard", 2), subscription, pricing, intents, client);

        var result = await HandleAsync(
            Command(subscription.TenantId, "Standard", 5),
            subscription,
            pricing,
            intents,
            client
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Seat.CheckoutInProgress", result.Error.Code);
        Assert.Single(intents.Added);
        Assert.Equal(1, client.CreateCalls);
    }

    [Fact]
    public async Task Once_the_session_expired_a_new_checkout_starts_clean()
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();
        var intents = new FakeSeatPurchaseIntentRepository();
        var expired = Client(expiresAtUtc: DateTime.UtcNow.AddMinutes(-1));
        var command = Command(subscription.TenantId, "Standard", 2);
        await HandleAsync(command, subscription, pricing, intents, expired);

        var fresh = Client();
        var result = await HandleAsync(command, subscription, pricing, intents, fresh);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, intents.Added.Count);
        Assert.Equal(1, fresh.CreateCalls);
    }

    [Theory]
    [InlineData(DiscountAppliesTo.BaseSeatsAddOns, true)]
    [InlineData(DiscountAppliesTo.Base, false)]
    public async Task Seat_proration_applies_the_discount_when_in_scope(DiscountAppliesTo appliesTo, bool discounted)
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();
        var terms = SubscriptionDiscountTerms
            .Create(
                DiscountSource.Admin,
                null,
                2_000,
                null,
                null,
                DiscountDuration.Forever,
                null,
                appliesTo,
                null,
                "Retention deal",
                DateTime.UtcNow.AddMinutes(-1)
            )
            .Value;
        Assert.True(subscription.GrantDiscount(terms, false, Guid.NewGuid(), DateTime.UtcNow).IsSuccess);
        var intents = new FakeSeatPurchaseIntentRepository();
        var client = Client();

        var result = await HandleAsync(
            Command(subscription.TenantId, "Standard", 2),
            subscription,
            pricing,
            intents,
            client
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var request = client.LastRequest!;
        var intent = Assert.Single(intents.Added);
        // Lo que se firma y se cobra es el neto; el descuento viaja aparte para el recibo.
        Assert.Equal(intent.ProratedTotal.Cents, request.AmountCents);
        if (discounted)
        {
            Assert.Equal("Admin", request.DiscountSource);
            Assert.Equal(request.AmountCents / 4, request.DiscountAmountCents);
        }
        else
        {
            Assert.Null(request.DiscountAmountCents);
            Assert.Null(request.DiscountSource);
        }
    }

    private static FakeSeatCheckoutPaymentClient Client(DateTime? expiresAtUtc = null) =>
        new(
            Result.Success(
                new SeatCheckoutClientResult(
                    Guid.NewGuid(),
                    "https://pay.example/xyz",
                    "cs_123",
                    expiresAtUtc ?? DateTime.UtcNow.AddHours(24)
                )
            )
        );

    private static Task<Result<StartSeatCheckoutResponse>> HandleAsync(
        StartSeatCheckoutCommand command,
        TenantSubscription subscription,
        SeatPricing pricing,
        FakeSeatPurchaseIntentRepository intents,
        FakeSeatCheckoutPaymentClient client
    ) =>
        Start(
            command,
            new FakeSubscriptionRepo(subscription),
            new FakeSeatPricingRepository(pricing),
            intents,
            client,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

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
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Starter, Guid.Empty, DateTime.UtcNow)
            .Value;
        var version = SubscriptionPlanVersion
            .Create(plan.Id, versionNumber: 1, trialDaysDefault: 14, [BillingCycle.Monthly, BillingCycle.Yearly])
            .Value;
        PlanTestData.EnsurePublishableFeature(version);
        PlanTestData.EnsurePublishablePrice(version);
        plan.AddVersion(version, Guid.Empty, DateTime.UtcNow);
        Assert.True(plan.PublishVersion(version.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow).IsSuccess);
        return (plan, version);
    }
}
