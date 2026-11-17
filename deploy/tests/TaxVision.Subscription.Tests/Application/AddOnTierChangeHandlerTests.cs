using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.AddOns;
using TaxVision.Subscription.Application.AddOns.TierChange;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.Domain;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>Cambio de tamaño del storage: subir ya con cobro de la diferencia, bajar en la renovación si los
/// archivos caben, cancelar lo programado, vista previa, checkout de la subida y la decisión al renovar.</summary>
public sealed class AddOnTierChangeHandlerTests
{
    private const long Gb = 1024L * 1024 * 1024;
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed class World
    {
        public required TenantSubscription Subscription { get; init; }
        public required TenantAddOn AddOn { get; init; }
        public required AddOnDefinition Small { get; init; }
        public required AddOnDefinition Large { get; init; }
        public required FakeTenantAddOnRepo AddOns { get; init; }
        public required FakeStorageUsageClient Usage { get; init; }
        public required FakeSnapshotRepo Snapshots { get; init; }
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public CapturingMessageBus Bus { get; } = new();
        public FakeAddOnPurchaseIntentRepository Intents { get; } = new();

        public AddOnTierChangeLoader Loader() =>
            new(AddOns, new FakeSubscriptionRepo(Subscription), new FakeAddOnDefinitionRepository(Small, Large));
    }

    private static World Build(bool ownsLarge, long? usedGb, long maxGb = 20)
    {
        var plan = AddOnTestCatalog.PlanWith("starter", ["documents"]);
        var subscription = AddOnTestCatalog.ActiveSubscription(TenantId, plan);
        var small = AddOnTierChangeTests.Pack("addon.storage.small", 3m, 2);
        var large = AddOnTierChangeTests.Pack("addon.storage.large", 10m, 10);
        var owned = ownsLarge ? large : small;
        var addOn = TenantAddOn
            .Purchase(
                TenantId,
                owned,
                1,
                owned.ResolveUnitPrice(BillingCycle.Monthly, 1).Value,
                BillingCycle.Monthly,
                true,
                UserId,
                subscription.CurrentPeriodStartUtc
            )
            .Value;
        addOn.CoTermTo(subscription.CurrentPeriodEndUtc, UserId, subscription.CurrentPeriodStartUtc);
        var snapshot = TenantEntitlementSnapshot
            .Rebuild(
                TenantId,
                0,
                "enterprise",
                Guid.NewGuid(),
                "Active",
                1,
                1,
                [
                    new EntitlementEntry(
                        EntitlementKey.Create("storage.max_bytes").Value,
                        EntitlementValueType.Long,
                        (maxGb * Gb).ToString(),
                        EntitlementStatus.Active,
                        EntitlementSource.Plan,
                        null
                    ),
                ],
                DateTime.UtcNow
            )
            .Value;
        return new World
        {
            Subscription = subscription,
            AddOn = addOn,
            Small = small,
            Large = large,
            AddOns = new FakeTenantAddOnRepo([addOn]),
            Usage = new FakeStorageUsageClient(new() { [TenantId] = usedGb is null ? null : usedGb * Gb }),
            Snapshots = new FakeSnapshotRepo(snapshot),
        };
    }

    private static Task<BuildingBlocks.Results.Result<AddOnTierChangeResponse>> Change(World w, string toCode)
    {
        // El orquestador mide fuera de la transacción e invoca el paso transaccional.
        w.Bus.OnInvoke = async (message, ct) =>
            await ApplyAddOnTierChangeHandler.Handle(
                (ApplyAddOnTierChangeCommand)message,
                w.Loader(),
                w.UnitOfWork,
                w.Bus,
                new FakeCorrelationContext(),
                new FakeSubscriptionAuditLogWriter(),
                new TaxVision.Subscription.Tests.TestDoubles.NoSubscriptionDiscounts(),
                NullLogger<TenantAddOn>.Instance,
                ct
            );
        return ChangeAddOnTierHandler.Handle(
            new ChangeAddOnTierCommand(TenantId, w.AddOn.Id, toCode, UserId),
            w.Loader(),
            w.Usage,
            w.Snapshots,
            w.Bus,
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Upgrade_switches_now_and_publishes_the_prorated_charge()
    {
        var w = Build(ownsLarge: false, usedGb: 1);

        var result = await Change(w, "addon.storage.large");

        Assert.True(result.IsSuccess);
        Assert.Equal("Upgrade", result.Value.Direction);
        Assert.Equal("addon.storage.large", w.AddOn.AddOnCode);
        var charge = Assert.Single(w.Bus.Published.OfType<AddOnRenewalDueIntegrationEvent>());
        Assert.StartsWith("addon-tierup-", charge.IdempotencyKey);
        Assert.Equal(result.Value.ChargedCents, charge.AmountCents);
        Assert.True(charge.AmountCents > 0);
        Assert.Equal(1, w.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Downgrade_that_fits_is_scheduled_for_renewal()
    {
        var w = Build(ownsLarge: true, usedGb: 5);

        var result = await Change(w, "addon.storage.small");

        Assert.True(result.IsSuccess);
        Assert.Equal("Downgrade", result.Value.Direction);
        Assert.Equal(0, result.Value.ChargedCents);
        Assert.Equal("addon.storage.large", w.AddOn.AddOnCode);
        Assert.Equal("addon.storage.small", w.AddOn.PendingAddOnCode);
        Assert.Empty(w.Bus.Published.OfType<AddOnRenewalDueIntegrationEvent>());
    }

    [Fact]
    public async Task Downgrade_that_does_not_fit_is_rejected_and_nothing_changes()
    {
        // 20 GB hoy (incluye los 10 del paquete grande); bajando al de 2 GB quedan 12.
        var w = Build(ownsLarge: true, usedGb: 13);

        var result = await Change(w, "addon.storage.small");

        Assert.Equal("AddOn.StorageWouldOverflow", result.Error.Code);
        Assert.False(w.AddOn.HasScheduledTierChange);
        Assert.Equal(0, w.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Downgrade_without_a_usage_reading_is_rejected()
    {
        var w = Build(ownsLarge: true, usedGb: null);

        var result = await Change(w, "addon.storage.small");

        Assert.Equal("AddOn.StorageUsageUnavailable", result.Error.Code);
        Assert.False(w.AddOn.HasScheduledTierChange);
    }

    [Fact]
    public async Task A_scheduled_downgrade_can_be_cancelled()
    {
        var w = Build(ownsLarge: true, usedGb: 1);
        await Change(w, "addon.storage.small");

        var result = await CancelAddOnTierChangeHandler.Handle(
            new CancelAddOnTierChangeCommand(TenantId, w.AddOn.Id, UserId),
            w.AddOns,
            w.UnitOfWork,
            new FakeCorrelationContext(),
            new FakeSubscriptionAuditLogWriter(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(w.AddOn.HasScheduledTierChange);
    }

    [Fact]
    public async Task Preview_shows_the_charge_of_an_upgrade_and_whether_a_downgrade_fits()
    {
        var up = Build(ownsLarge: false, usedGb: 1);
        var upgrade = await GetAddOnTierChangePreviewHandler.Handle(
            new GetAddOnTierChangePreviewQuery(TenantId, up.AddOn.Id, "addon.storage.large"),
            up.Loader(),
            up.Usage,
            up.Snapshots,
            CancellationToken.None
        );
        var down = Build(ownsLarge: true, usedGb: 13);
        var downgrade = await GetAddOnTierChangePreviewHandler.Handle(
            new GetAddOnTierChangePreviewQuery(TenantId, down.AddOn.Id, "addon.storage.small"),
            down.Loader(),
            down.Usage,
            down.Snapshots,
            CancellationToken.None
        );

        Assert.Equal("Upgrade", upgrade.Value.Direction);
        Assert.True(upgrade.Value.ChargeNowCents > 0);
        Assert.Null(upgrade.Value.WouldFit);
        Assert.Equal("Downgrade", downgrade.Value.Direction);
        Assert.False(downgrade.Value.WouldFit);
        Assert.Equal(down.AddOn.CurrentPeriodEndUtc, downgrade.Value.EffectiveAtUtc);
        // La vista previa no cambia nada.
        Assert.Equal("addon.storage.small", up.AddOn.AddOnCode);
    }

    [Fact]
    public async Task Upgrade_checkout_reserves_an_intent_for_the_difference()
    {
        var w = Build(ownsLarge: false, usedGb: 1);

        var reserved = await ReserveAddOnTierUpgradeCheckoutHandler.Handle(
            new ReserveAddOnTierUpgradeCheckoutCommand(TenantId, w.AddOn.Id, "addon.storage.large", UserId),
            w.Loader(),
            w.Intents,
            w.UnitOfWork,
            CancellationToken.None
        );

        Assert.True(reserved.IsSuccess);
        var intent = Assert.Single(w.Intents.Added);
        Assert.Equal(w.AddOn.Id, intent.UpgradesTenantAddOnId);
        Assert.Equal("addon.storage.large", intent.AddOnCode);
        Assert.Equal(reserved.Value.TotalCents, intent.ProratedTotal.Cents);
    }

    // Con un pago abierto en PayPal, volver con Stripe cierra ese y abre otro; con el mismo proveedor sigue el freno.
    [Fact]
    public async Task Upgrade_checkout_with_another_provider_replaces_the_open_one()
    {
        var w = Build(ownsLarge: false, usedGb: 1);
        var first = await ReserveAsync("PayPal");
        var open = Assert.Single(w.Intents.Added);
        var paymentId = Guid.NewGuid();
        open.AttachCheckout(paymentId, "https://paypal/x", DateTime.UtcNow.AddHours(1), DateTime.UtcNow, "PayPal");

        var samePayPal = await ReserveAsync("PayPal");
        var stripe = await ReserveAsync("Stripe");

        Assert.True(first.IsSuccess);
        Assert.Equal("AddOn.CheckoutInProgress", samePayPal.Error.Code);
        Assert.True(stripe.IsSuccess, stripe.IsFailure ? stripe.Error.Code : null);
        Assert.Equal(paymentId, stripe.Value.ReplacedPaymentId);
        Assert.Equal(open.Id, stripe.Value.ReplacedIntentId);
        Assert.Equal(AddOnPurchaseIntentStatus.Cancelled, open.Status);
        Assert.Equal(2, w.Intents.Added.Count);

        Task<BuildingBlocks.Results.Result<TaxVision.Subscription.Application.AddOns.Commands.StartAddOnCheckout.AddOnCheckoutReservation>> ReserveAsync(
            string provider
        ) =>
            ReserveAddOnTierUpgradeCheckoutHandler.Handle(
                new ReserveAddOnTierUpgradeCheckoutCommand(
                    TenantId,
                    w.AddOn.Id,
                    "addon.storage.large",
                    UserId,
                    provider
                ),
                w.Loader(),
                w.Intents,
                w.UnitOfWork,
                CancellationToken.None
            );
    }

    [Fact]
    public async Task Upgrade_checkout_does_not_take_a_downgrade()
    {
        var w = Build(ownsLarge: true, usedGb: 1);

        var reserved = await ReserveAddOnTierUpgradeCheckoutHandler.Handle(
            new ReserveAddOnTierUpgradeCheckoutCommand(TenantId, w.AddOn.Id, "addon.storage.small", UserId),
            w.Loader(),
            w.Intents,
            w.UnitOfWork,
            CancellationToken.None
        );

        Assert.Equal("AddOn.TierChangeWrongDirection", reserved.Error.Code);
        Assert.Empty(w.Intents.Added);
    }

    [Fact]
    public async Task A_paid_upgrade_checkout_switches_the_existing_add_on_instead_of_adding_one()
    {
        var w = Build(ownsLarge: false, usedGb: 1);
        var plan = AddOnTierChangePolicy.Plan(w.AddOn, w.Small, w.Large, 1, DateTime.UtcNow).Value;
        var intent = AddOnPurchaseIntent.CreateTierUpgrade(w.AddOn, w.Large, plan, UserId, DateTime.UtcNow).Value;

        var provisioned = await AddOnCheckoutProvisioning.TryProvisionAsync(
            intent,
            w.Subscription,
            w.Large,
            w.AddOns,
            new FakeSubscriptionAuditLogWriter(),
            new FakeSubscriptionMetrics(),
            "corr",
            "webhook",
            DateTime.UtcNow,
            DateTime.UtcNow,
            CancellationToken.None
        );

        Assert.Same(w.AddOn, provisioned);
        Assert.Empty(w.AddOns.Added);
        Assert.Equal("addon.storage.large", w.AddOn.AddOnCode);
        Assert.Equal(AddOnPurchaseIntentStatus.Provisioned, intent.Status);
    }

    [Theory]
    [InlineData(5L, ScheduledTierChangeOutcome.Applied, "addon.storage.small")]
    [InlineData(13L, ScheduledTierChangeOutcome.Skipped, "addon.storage.large")]
    [InlineData(null, ScheduledTierChangeOutcome.Postponed, "addon.storage.large")]
    public void At_renewal_a_scheduled_downgrade_applies_only_if_the_files_fit(
        long? usedGb,
        ScheduledTierChangeOutcome expected,
        string codeAfter
    )
    {
        var w = Build(ownsLarge: true, usedGb: 1);
        var plan = AddOnTierChangePolicy.Plan(w.AddOn, w.Large, w.Small, 1, DateTime.UtcNow).Value;
        w.AddOn.ScheduleTierChange(w.Small, plan, UserId, DateTime.UtcNow);

        var outcome = ScheduledTierChangeAtRenewal.Resolve(
            w.AddOn,
            w.Large,
            w.Small,
            usedGb is null ? null : usedGb * Gb,
            20 * Gb,
            DateTime.UtcNow
        );

        Assert.Equal(expected, outcome);
        Assert.Equal(codeAfter, w.AddOn.AddOnCode);
        Assert.Equal(expected == ScheduledTierChangeOutcome.Postponed, w.AddOn.HasScheduledTierChange);
    }
}
