using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>Cambiar de tamaño un paquete de storage: subir es inmediato con la diferencia prorrateada; bajar
/// queda para la renovación y solo si los archivos caben.</summary>
public sealed class AddOnTierChangeTests
{
    private const long Gb = 1024L * 1024 * 1024;
    private static readonly DateTime PeriodStart = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime MidPeriod = new(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Actor = Guid.NewGuid();

    internal static AddOnDefinition Pack(string code, decimal monthlyUsd, long gb, string? group = "storage")
    {
        var definition = AddOnDefinition
            .Seed(
                Guid.NewGuid(),
                AddOnCode.Create(code).Value,
                code,
                "pack",
                "quota",
                allowMultipleInstances: false,
                [BillingCycle.Monthly],
                PeriodStart,
                group
            )
            .Value;
        definition.AddPriceTier(
            AddOnPriceTier
                .Create(definition.Id, BillingCycle.Monthly, 1, null, Money.Create(monthlyUsd, "USD").Value)
                .Value
        );
        definition.AddEntitlementDefinition(
            AddOnEntitlementDefinition
                .Create(
                    definition.Id,
                    EntitlementKey.Create("storage.max_bytes").Value,
                    EntitlementValueType.Long,
                    (gb * Gb).ToString(),
                    AddOnMergeStrategy.Sum
                )
                .Value
        );
        definition.Publish(Guid.Empty, PeriodStart);
        return definition;
    }

    internal static TenantAddOn Active(AddOnDefinition definition)
    {
        var addOn = TenantAddOn
            .Purchase(
                Guid.NewGuid(),
                definition,
                1,
                definition.ResolveUnitPrice(BillingCycle.Monthly, 1).Value,
                BillingCycle.Monthly,
                autoRenew: true,
                Actor,
                PeriodStart
            )
            .Value;
        addOn.CoTermTo(PeriodEnd, Actor, PeriodStart);
        return addOn;
    }

    private static readonly AddOnDefinition Small = Pack("addon.storage.small", 3m, 2);
    private static readonly AddOnDefinition Medium = Pack("addon.storage.medium", 6m, 5);
    private static readonly AddOnDefinition Large = Pack("addon.storage.large", 10m, 10);

    [Fact]
    public void Upgrade_applies_now_and_charges_the_prorated_difference()
    {
        var addOn = Active(Small);

        var plan = AddOnTierChangePolicy.Plan(addOn, Small, Large, activeInstancesInGroup: 1, MidPeriod).Value;
        var upgraded = addOn.UpgradeTier(Large, plan, Actor, MidPeriod);

        Assert.True(upgraded.IsSuccess);
        Assert.Equal(AddOnTierChangeDirection.Upgrade, plan.Direction);
        Assert.Equal(MidPeriod, plan.EffectiveAtUtc);
        // ($10 − $3) × 15 / 30 días.
        Assert.Equal(3.50m, plan.ChargeNow.Amount);
        Assert.Equal("addon.storage.large", addOn.AddOnCode);
        Assert.Equal(Large.Id, addOn.AddOnDefinitionId);
        Assert.Equal(10m, addOn.UnitPrice.Amount);
        Assert.Equal(PeriodEnd, addOn.CurrentPeriodEndUtc);
    }

    [Fact]
    public void Downgrade_is_scheduled_for_renewal_without_charge()
    {
        var addOn = Active(Large);

        var plan = AddOnTierChangePolicy.Plan(addOn, Large, Small, 1, MidPeriod).Value;
        var scheduled = addOn.ScheduleTierChange(Small, plan, Actor, MidPeriod);

        Assert.True(scheduled.IsSuccess);
        Assert.Equal(AddOnTierChangeDirection.Downgrade, plan.Direction);
        Assert.Equal(0m, plan.ChargeNow.Amount);
        Assert.Equal(PeriodEnd, plan.EffectiveAtUtc);
        Assert.Equal("addon.storage.large", addOn.AddOnCode);
        Assert.Equal("addon.storage.small", addOn.PendingAddOnCode);
        Assert.True(addOn.HasScheduledTierChange);
    }

    [Fact]
    public void Scheduled_downgrade_applies_the_new_size_and_price()
    {
        var addOn = Active(Large);
        addOn.ScheduleTierChange(
            Small,
            AddOnTierChangePolicy.Plan(addOn, Large, Small, 1, MidPeriod).Value,
            Actor,
            MidPeriod
        );

        var applied = addOn.ApplyScheduledTierChange(Actor, PeriodEnd);

        Assert.True(applied.IsSuccess);
        Assert.Equal("addon.storage.small", addOn.AddOnCode);
        Assert.Equal(Small.Id, addOn.AddOnDefinitionId);
        Assert.Equal(3m, addOn.UnitPrice.Amount);
        Assert.False(addOn.HasScheduledTierChange);
    }

    [Fact]
    public void A_downgrade_that_no_longer_fits_keeps_the_current_size_and_says_why()
    {
        var addOn = Active(Large);
        addOn.ScheduleTierChange(
            Small,
            AddOnTierChangePolicy.Plan(addOn, Large, Small, 1, MidPeriod).Value,
            Actor,
            MidPeriod
        );

        var skipped = addOn.SkipScheduledTierChange(AddOnTierChangePolicy.StorageWouldOverflowReason, Actor, PeriodEnd);

        Assert.True(skipped.IsSuccess);
        Assert.Equal("addon.storage.large", addOn.AddOnCode);
        Assert.False(addOn.HasScheduledTierChange);
        Assert.Equal(AddOnTierChangePolicy.StorageWouldOverflowReason, addOn.TierChangeSkippedReason);
    }

    [Fact]
    public void A_scheduled_downgrade_can_be_cancelled()
    {
        var addOn = Active(Large);
        addOn.ScheduleTierChange(
            Small,
            AddOnTierChangePolicy.Plan(addOn, Large, Small, 1, MidPeriod).Value,
            Actor,
            MidPeriod
        );

        Assert.True(addOn.CancelScheduledTierChange(Actor, MidPeriod).IsSuccess);
        Assert.False(addOn.HasScheduledTierChange);
        Assert.Equal("AddOn.NoTierChangeScheduled", addOn.CancelScheduledTierChange(Actor, MidPeriod).Error.Code);
    }

    [Fact]
    public void Upgrading_clears_a_scheduled_downgrade()
    {
        var addOn = Active(Medium);
        addOn.ScheduleTierChange(
            Small,
            AddOnTierChangePolicy.Plan(addOn, Medium, Small, 1, MidPeriod).Value,
            Actor,
            MidPeriod
        );

        addOn.UpgradeTier(
            Large,
            AddOnTierChangePolicy.Plan(addOn, Medium, Large, 1, MidPeriod).Value,
            Actor,
            MidPeriod
        );

        Assert.False(addOn.HasScheduledTierChange);
    }

    [Theory]
    [InlineData(9, true)]
    [InlineData(12, true)]
    [InlineData(13, false)]
    public void A_downgrade_fits_only_if_the_files_fit_in_the_new_total(long usedGb, bool fits)
    {
        // Plan 10 GB + paquete de 10 GB = 20 GB; bajando a 2 GB quedan 12 GB.
        Assert.Equal(fits, AddOnTierChangePolicy.Fits(usedGb * Gb, 20 * Gb, Large, Small));
    }

    [Fact]
    public void Changing_to_the_same_size_is_rejected()
    {
        var addOn = Active(Small);

        Assert.Equal(
            "AddOn.TierChangeSameTier",
            AddOnTierChangePolicy.Plan(addOn, Small, Small, 1, MidPeriod).Error.Code
        );
    }

    [Fact]
    public void Changing_outside_the_storage_group_is_rejected()
    {
        var other = Pack("addon.sms.pack", 5m, 0, group: "sms");
        var addOn = Active(Small);

        Assert.Equal(
            "AddOn.TierChangeOutsideGroup",
            AddOnTierChangePolicy.Plan(addOn, Small, other, 1, MidPeriod).Error.Code
        );
    }

    [Fact]
    public void Changing_with_several_packs_of_the_group_is_rejected()
    {
        var addOn = Active(Small);

        Assert.Equal(
            "AddOn.TierChangeRequiresSingleInstance",
            AddOnTierChangePolicy.Plan(addOn, Small, Large, activeInstancesInGroup: 2, MidPeriod).Error.Code
        );
    }

    [Fact]
    public void Changing_a_cancelled_pack_is_rejected()
    {
        var addOn = Active(Small);
        addOn.CancelActive("no longer needed", Actor, MidPeriod);

        Assert.Equal(
            "AddOn.TierChangeNotActive",
            AddOnTierChangePolicy.Plan(addOn, Small, Large, 1, MidPeriod).Error.Code
        );
    }

    [Fact]
    public void Changing_during_a_renewal_is_rejected()
    {
        var addOn = Active(Small);
        addOn.BeginRenewal("renewal-1", PeriodEnd.AddMonths(1), Actor, MidPeriod);

        Assert.Equal(
            "AddOn.RenewalInProgress",
            AddOnTierChangePolicy.Plan(addOn, Small, Large, 1, MidPeriod).Error.Code
        );
    }

    [Fact]
    public void A_plan_cannot_be_used_in_the_wrong_direction()
    {
        var addOn = Active(Large);
        var downgrade = AddOnTierChangePolicy.Plan(addOn, Large, Small, 1, MidPeriod).Value;

        Assert.Equal(
            "AddOn.TierChangeWrongDirection",
            addOn.UpgradeTier(Small, downgrade, Actor, MidPeriod).Error.Code
        );
    }
}
