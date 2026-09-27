using TaxVision.Subscription.Application.Subscriptions.Queries;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// A5 (A6.4 del plan) — <c>GET subscriptions/me</c> no exigía ningún permiso, así que cualquier empleado
/// veía el precio del plan, el nombre comercial, los límites y el último fallo de cobro de la oficina.
///
/// <para>
/// Sin <c>billing.view</c> la respuesta conserva la MISMA forma con esos campos vacíos, en vez de un
/// 403: el shell del CRM desplegado pide este endpoint en cada sesión para el banner de ciclo de vida y
/// solo lee <c>status</c>, <c>billingAccessBlocked</c> y <c>gracePeriodEndsAtUtc</c>. Un 403 le apagaría
/// el banner a todos los empleados en silencio (§R.7: ningún chequeo nuevo puede quitar acceso que hoy
/// funciona).
/// </para>
/// </summary>
public sealed class MySubscriptionBillingVisibilityTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task With_billing_view_everything_comes_through()
    {
        var response = await HandleAsync(canViewBilling: true);

        Assert.Equal("pro", response.PlanCode);
        Assert.Equal("Pro", response.PlanName);
        Assert.Equal(49m, response.MonthlyPriceUsd);
        Assert.Equal(5, response.MaxUsers);
        Assert.Equal("Monthly", response.BillingCycle);
    }

    [Fact]
    public async Task Without_billing_view_the_commercial_fields_are_empty()
    {
        var response = await HandleAsync(canViewBilling: false);

        Assert.Equal(string.Empty, response.PlanCode);
        Assert.Equal(string.Empty, response.PlanName);
        Assert.Equal(0m, response.MonthlyPriceUsd);
        Assert.Equal(0m, response.CurrentCyclePriceUsd);
        Assert.Equal(0, response.MaxUsers);
        Assert.Equal(0, response.MaxPendingInvitations);
        Assert.Equal(0, response.StorageQuotaBytes);
        Assert.Null(response.SuspensionReason);
        Assert.Null(response.LastPaymentFailure);
    }

    /// <summary>Lo que el banner del CRM lee de verdad tiene que seguir llegando sin el permiso, o se
    /// apaga para todos los empleados.</summary>
    [Fact]
    public async Task Without_billing_view_the_lifecycle_banner_still_works()
    {
        var response = await HandleAsync(canViewBilling: false);

        Assert.Equal("Active", response.Status);
        Assert.False(response.BillingAccessBlocked);
        Assert.Equal(["documents"], response.EnabledModules);
        Assert.NotEqual(default, response.CurrentPeriodEndUtc);
    }

    [Fact]
    public async Task The_status_endpoint_carries_no_commercial_data_at_all()
    {
        var result = await GetMySubscriptionStatusHandler.Handle(
            new GetMySubscriptionStatusQuery(TenantId, CanManageBilling: true),
            new FakeSubscriptionRepo(ActiveSubscription()),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Active", result.Value.Status);
        Assert.False(result.Value.BillingAccessBlocked);
        Assert.True(result.Value.CanManageBilling);

        // El contrato entero son cinco campos: ninguno nombra el plan ni un monto.
        var properties = typeof(MySubscriptionStatusResponse).GetProperties().Select(property => property.Name);
        Assert.Equal(
            ["Status", "BillingAccessBlocked", "GracePeriodEndsAtUtc", "NextRenewalAtUtc", "CanManageBilling"],
            properties
        );
    }

    [Fact]
    public async Task The_status_endpoint_says_when_access_is_cut()
    {
        var subscription = ActiveSubscription();
        Assert.True(subscription.SuspendForPolicyViolation("Unpaid", Guid.Empty, DateTime.UtcNow).IsSuccess);

        var result = await GetMySubscriptionStatusHandler.Handle(
            new GetMySubscriptionStatusQuery(TenantId, CanManageBilling: false),
            new FakeSubscriptionRepo(subscription),
            CancellationToken.None
        );

        Assert.Equal("Suspended", result.Value.Status);
        Assert.True(result.Value.BillingAccessBlocked);
        Assert.False(result.Value.CanManageBilling);
    }

    [Fact]
    public async Task The_status_endpoint_fails_when_there_is_no_subscription()
    {
        var result = await GetMySubscriptionStatusHandler.Handle(
            new GetMySubscriptionStatusQuery(TenantId, CanManageBilling: true),
            new FakeSubscriptionRepo(subscription: null),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.NotFound", result.Error.Code);
    }

    // ---------------------------------------------------------------------

    private static async Task<MySubscriptionResponse> HandleAsync(bool canViewBilling)
    {
        var plan = ProPlan();
        var result = await GetMySubscriptionHandler.Handle(
            new GetMySubscriptionQuery(TenantId, canViewBilling),
            new FakeSubscriptionRepo(ActiveSubscription(plan)),
            new FakePlanRepo(plan),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    private static SubscriptionPlan ProPlan()
    {
        var nowUtc = DateTime.UtcNow;
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("pro").Value, "Pro", "Pro plan", PlanTier.Standard, Guid.Empty, nowUtc)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly, BillingCycle.Yearly]).Value;

        version.AddEntitlementDefinition(
            PlanEntitlementDefinition
                .Create(
                    version.Id,
                    EntitlementKey.Create("seats.max").Value,
                    EntitlementValueType.Int,
                    "5",
                    "seats.max"
                )
                .Value
        );
        version.AddFeature(
            PlanFeature.Create(version.Id, EntitlementKey.Create("module.documents").Value, true, "documents").Value
        );
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(49m, "USD").Value).Value
        );

        plan.AddVersion(version, Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc);
        return plan;
    }

    private static TenantSubscription ActiveSubscription(SubscriptionPlan? plan = null)
    {
        plan ??= ProPlan();
        var version = plan.GetPublishedVersion()!;
        var nowUtc = DateTime.UtcNow;
        return TenantSubscription
            .ActivateImmediately(
                TenantId,
                plan,
                version,
                BillingCycle.Monthly,
                nowUtc,
                nowUtc.AddDays(30),
                Guid.Empty,
                nowUtc
            )
            .Value;
    }
}
