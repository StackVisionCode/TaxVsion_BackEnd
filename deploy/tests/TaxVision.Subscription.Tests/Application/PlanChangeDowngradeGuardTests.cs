using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;
using TaxVision.Subscription.Application.Subscriptions.Queries;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// Aceptación de la fase: un downgrade que no entra se explica y se bloquea. El cupo del plan destino lo
/// sabe Subscription; cuánta gente lo ocupa, solo Auth — por eso el guard lo consulta antes de agendar.
/// </summary>
public sealed class PlanChangeDowngradeGuardTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task A_downgrade_that_leaves_users_out_is_blocked_and_says_why()
    {
        var scenario = Scenario();

        var result = await ChangePlanAsync(scenario, "starter", activeUsers: 8);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.DowngradeExceedsSeats", result.Error.Code);
        Assert.Contains("8", result.Error.Message);
        Assert.Empty(scenario.Subscription.PendingDowngrades);
    }

    [Fact]
    public async Task A_downgrade_that_fits_is_scheduled()
    {
        var scenario = Scenario();

        var result = await ChangePlanAsync(scenario, "starter", activeUsers: 2);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.AwaitingPayment);
        Assert.Single(scenario.Subscription.PendingDowngrades);
    }

    // El downgrade se agenda para el fin del período y se puede deshacer: un corte de Auth no lo bloquea.
    [Fact]
    public async Task A_downgrade_still_goes_through_when_Auth_does_not_answer()
    {
        var scenario = Scenario();

        var result = await ChangePlanAsync(scenario, "starter", activeUsers: null);

        Assert.True(result.IsSuccess);
        Assert.Single(scenario.Subscription.PendingDowngrades);
    }

    // Subir de plan nunca baja el cupo: el guard no aplica.
    [Fact]
    public async Task An_upgrade_is_never_blocked_by_the_seat_guard()
    {
        var scenario = Scenario(currentPlanCode: "starter");

        var result = await ChangePlanAsync(scenario, "pro", activeUsers: 99);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.AwaitingPayment);
    }

    [Fact]
    public async Task The_preview_says_it_does_not_fit_before_anything_is_requested()
    {
        var scenario = Scenario();

        var preview = await PreviewAsync(scenario, "starter", activeUsers: 8);

        Assert.True(preview.IsSuccess);
        Assert.Equal("Downgrade", preview.Value.Direction);
        Assert.True(preview.Value.Blocked);
        Assert.Equal("Subscription.DowngradeExceedsSeats", preview.Value.BlockedReason);
        Assert.Equal(3, preview.Value.SeatsAfterChange);
        Assert.Equal(8, preview.Value.OccupiedSeats);
        Assert.False(preview.Value.ChargedNow);
        Assert.Equal(scenario.Subscription.CurrentPeriodEndUtc, preview.Value.EffectiveAtUtc);
    }

    [Fact]
    public async Task The_preview_of_an_upgrade_shows_the_full_price_charged_now()
    {
        var scenario = Scenario(currentPlanCode: "starter");

        var preview = await PreviewAsync(scenario, "pro", activeUsers: 2);

        Assert.True(preview.IsSuccess);
        Assert.Equal("Upgrade", preview.Value.Direction);
        Assert.True(preview.Value.ChargedNow);
        Assert.Null(preview.Value.EffectiveAtUtc);
        Assert.Equal(4900, preview.Value.AmountCents);
        Assert.Equal(1900, preview.Value.CurrentAmountCents);
        Assert.False(preview.Value.Blocked);
    }

    // Sin método en archivo el upgrade solo se puede pagar por redirect: con URLs de retorno, el handler
    // pide la sesión en vez de publicar el cobro off-session.
    [Fact]
    public async Task An_upgrade_with_return_urls_is_charged_by_hosted_checkout()
    {
        var scenario = Scenario(currentPlanCode: "starter");
        var checkoutClient = new FakePlanChangeCheckoutPaymentClient();

        var result = await ChangePlanAsync(scenario, "pro", activeUsers: 2, checkoutClient, hostedCheckout: true);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.AwaitingPayment);
        Assert.Equal("https://pay.example/upgrade", result.Value.CheckoutUrl);
        Assert.Equal(1, checkoutClient.CreateCalls);
        Assert.Equal(4900, checkoutClient.LastRequest!.AmountCents);

        // La solicitud guarda la sesión para poder retomar el pago sin abrir otro.
        var request = Assert.Single(scenario.Subscription.PlanChangeRequests);
        Assert.Equal("https://pay.example/upgrade", request.CheckoutUrl);
        Assert.True(request.HasOpenCheckout(DateTime.UtcNow));
        Assert.Equal(checkoutClient.LastRequest.IdempotencyKey, request.PaymentIdempotencyKey);
    }

    [Fact]
    public async Task An_upgrade_without_return_urls_still_goes_off_session()
    {
        var scenario = Scenario(currentPlanCode: "starter");
        var checkoutClient = new FakePlanChangeCheckoutPaymentClient();

        var result = await ChangePlanAsync(scenario, "pro", activeUsers: 2, checkoutClient);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.AwaitingPayment);
        Assert.Null(result.Value.CheckoutUrl);
        Assert.Equal(0, checkoutClient.CreateCalls);
    }

    // Si el proveedor no puede abrir la sesión, el upgrade no queda "esperando un pago" que nadie pidió.
    [Fact]
    public async Task A_checkout_that_cannot_be_opened_fails_the_request()
    {
        var scenario = Scenario(currentPlanCode: "starter");
        var failing = new FakePlanChangeCheckoutPaymentClient(
            Result.Failure<PlanChangeCheckoutClientResult>(
                new Error("PlanChange.Checkout.Unavailable", "The payment service is unavailable.")
            )
        );

        var result = await ChangePlanAsync(scenario, "pro", activeUsers: 2, failing, hostedCheckout: true);

        Assert.True(result.IsFailure);
        Assert.Equal("PlanChange.Checkout.Unavailable", result.Error.Code);
    }

    private sealed record Fixture(TenantSubscription Subscription, FakeMultiPlanRepo Plans);

    /// <summary>starter: 3 asientos, $19. pro: 10 asientos, $49. Sin asientos comprados.</summary>
    private static Fixture Scenario(string currentPlanCode = "pro")
    {
        var starter = PlanWith("starter", seatsMax: 3, priceUsd: 19m);
        var pro = PlanWith("pro", seatsMax: 10, priceUsd: 49m);
        var current = currentPlanCode == "pro" ? pro : starter;
        var nowUtc = DateTime.UtcNow;

        var subscription = TenantSubscription
            .ActivateImmediately(
                TenantId,
                current,
                current.GetPublishedVersion()!,
                BillingCycle.Monthly,
                nowUtc,
                nowUtc.AddDays(30),
                Guid.Empty,
                nowUtc
            )
            .Value;

        return new Fixture(subscription, new FakeMultiPlanRepo(starter, pro));
    }

    private static Task<Result<ChangePlanResult>> ChangePlanAsync(
        Fixture fixture,
        string toPlan,
        int? activeUsers,
        FakePlanChangeCheckoutPaymentClient? checkoutClient = null,
        bool hostedCheckout = false
    ) =>
        ChangePlanHandler.Handle(
            hostedCheckout
                ? new ChangePlanCommand(
                    TenantId,
                    toPlan,
                    null,
                    Guid.NewGuid(),
                    "owner@acme.test",
                    "https://app/ok",
                    "https://app/cancel"
                )
                : new ChangePlanCommand(TenantId, toPlan, null, Guid.NewGuid()),
            new FakeSubscriptionRepo(fixture.Subscription),
            fixture.Plans,
            new FakeSeatRepo(),
            UserCounts(activeUsers),
            checkoutClient ?? new FakePlanChangeCheckoutPaymentClient(),
            new FakeUnitOfWork(),
            new CapturingMessageBus(),
            new FakeCorrelationContext(),
            new FakeSubscriptionAuditLogWriter(),
            NullLogger<TenantSubscription>.Instance,
            CancellationToken.None
        );

    private static Task<Result<PlanChangePreviewResponse>> PreviewAsync(
        Fixture fixture,
        string toPlan,
        int? activeUsers
    ) =>
        GetPlanChangePreviewHandler.Handle(
            new GetPlanChangePreviewQuery(TenantId, toPlan, null),
            new FakeSubscriptionRepo(fixture.Subscription),
            fixture.Plans,
            new FakeSeatRepo(),
            UserCounts(activeUsers),
            CancellationToken.None
        );

    private static ITenantUserCountClient UserCounts(int? activeUsers) =>
        activeUsers is { } count
            ? FakeTenantUserCountClient.WithActive(count)
            : FakeTenantUserCountClient.Unavailable();

    private static SubscriptionPlan PlanWith(string code, int seatsMax, decimal priceUsd)
    {
        var nowUtc = DateTime.UtcNow;
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Standard, Guid.Empty, nowUtc)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly]).Value;
        version.AddEntitlementDefinition(
            PlanEntitlementDefinition
                .Create(
                    version.Id,
                    EntitlementKey.Create("seats.max").Value,
                    EntitlementValueType.Int,
                    seatsMax.ToString(),
                    "seats.max"
                )
                .Value
        );
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(priceUsd, "USD").Value).Value
        );
        plan.AddVersion(version, Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc);
        return plan;
    }

    /// <summary>El resolver busca el plan destino por código y el actual por Id, así que hacen falta los dos.</summary>
    private sealed class FakeMultiPlanRepo(params SubscriptionPlan[] plans) : IPlanRepository
    {
        public Task<IReadOnlyList<SubscriptionPlan>> GetPublishedAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlan>>(plans);

        public Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken ct = default) =>
            Task.FromResult(plans.FirstOrDefault(plan => plan.Code.Value == code));

        public Task<SubscriptionPlan?> GetByIdAsync(Guid planId, CancellationToken ct = default) =>
            Task.FromResult(plans.FirstOrDefault(plan => plan.Id == planId));

        public Task<SubscriptionPlan?> GetByIdForUpdateAsync(Guid planId, CancellationToken ct = default) =>
            GetByIdAsync(planId, ct);
    }
}
