using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.Subscriptions.Events;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

// Términos comerciales: precio por ciclo, asientos, almacenamiento e invitaciones. Deciden si re-anclar es seguro.
public sealed class CommercialTermsTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public void Two_editions_with_the_same_prices_and_limits_have_equal_terms()
    {
        var plan = PlanWithEdition(monthly: 49m, yearly: 490m, seats: "3", storage: "1024");
        var other = AddEdition(plan, monthly: 49m, yearly: 490m, seats: "3", storage: "1024", modules: ["meetings"]);

        Assert.Equal(CommercialTerms.Of(plan.Versions.First()), CommercialTerms.Of(other));
    }

    [Theory]
    [InlineData(59, 490, "3", "1024")]
    [InlineData(49, 590, "3", "1024")]
    [InlineData(49, 490, "4", "1024")]
    [InlineData(49, 490, "3", "2048")]
    public void A_different_price_or_limit_makes_different_terms(int monthly, int yearly, string seats, string storage)
    {
        var plan = PlanWithEdition(monthly: 49m, yearly: 490m, seats: "3", storage: "1024");
        var other = AddEdition(plan, monthly, yearly, seats, storage, modules: []);

        Assert.NotEqual(CommercialTerms.Of(plan.Versions.First()), CommercialTerms.Of(other));
    }

    [Fact]
    public void Terms_read_prices_per_cycle_and_the_commercial_keys()
    {
        var plan = PlanWithEdition(monthly: 49m, yearly: 490m, seats: "3", storage: "1024");

        var terms = CommercialTerms.Of(plan.Versions.First());

        Assert.Equal(Money.Create(49m, "USD").Value, terms.Prices[BillingCycle.Monthly]);
        Assert.Equal(Money.Create(490m, "USD").Value, terms.Prices[BillingCycle.Yearly]);
        Assert.Equal(3, terms.SeatsMax);
        Assert.Equal(1024, terms.StorageMaxBytes);
        Assert.Equal(5, terms.InvitationsMaxPending);
    }

    [Fact]
    public void Revising_terms_publishes_a_copy_with_the_new_prices_and_limits_only()
    {
        var plan = PlanWithEdition(monthly: 299m, yearly: 2990m, seats: "25", storage: "214748364800");
        var before = plan.GetPublishedVersion()!;
        var target = CommercialTerms
            .Of(before)
            .WithPrices(Money.Create(99m, "USD").Value, Money.Create(999m, "USD").Value)
            .WithSeatsMax(3)
            .WithStorageMaxBytes(16_106_127_360);

        var revised = plan.ReviseCommercialTerms(target, Actor, DateTime.UtcNow);

        Assert.True(revised.Value);
        var after = plan.GetPublishedVersion()!;
        Assert.NotEqual(before.Id, after.Id);
        Assert.Equal(before.VersionNumber + 1, after.VersionNumber);
        Assert.Equal(target, CommercialTerms.Of(after));
        Assert.Equal([BillingCycle.Monthly, BillingCycle.Yearly], after.SupportedBillingCycles.Order());
        // Lo demás se copia tal cual (D10): módulos, claves de plataforma y días de trial.
        Assert.Equal(
            before.Features.Select(f => f.FeatureKey.Value).Order(),
            after.Features.Select(f => f.FeatureKey.Value).Order()
        );
        Assert.Equal("60", Value(after, CommunicationEntitlementCatalog.MaxRecordingMinutesMeeting));
        Assert.Equal(before.TrialDaysDefault, after.TrialDaysDefault);
    }

    [Fact]
    public void Revising_to_the_terms_already_published_creates_nothing()
    {
        var plan = PlanWithEdition(monthly: 49m, yearly: 490m, seats: "3", storage: "1024");
        var published = plan.GetPublishedVersion()!;

        var revised = plan.ReviseCommercialTerms(CommercialTerms.Of(published), Actor, DateTime.UtcNow);

        Assert.False(revised.Value);
        Assert.Single(plan.Versions);
    }

    [Fact]
    public void Revising_terms_needs_a_published_edition()
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("draft").Value, "Draft", "Draft plan", PlanTier.Starter, Actor, DateTime.UtcNow)
            .Value;
        var terms = CommercialTerms.Of(PlanWithEdition(49m, 490m, "3", "1024").GetPublishedVersion()!);

        Assert.Equal("Plan.NoPublishedVersion", plan.ReviseCommercialTerms(terms, Actor, DateTime.UtcNow).Error.Code);
    }

    [Fact]
    public void Rebaselining_to_an_edition_with_the_same_terms_moves_the_anchor_and_raises_one_event()
    {
        var plan = PlanWithEdition(monthly: 49m, yearly: 490m, seats: "3", storage: "1024");
        var old = plan.GetPublishedVersion()!;
        var subscription = ActiveOn(plan, old);
        var current = AddEdition(plan, 49m, 490m, "3", "1024", modules: ["meetings"]);

        var result = subscription.RebaselineVersion(plan, current.Id, "R1 rebaseline", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(current.Id, subscription.PlanVersionId);
        var evt = Assert.IsType<SubscriptionVersionRebaselinedDomainEvent>(Assert.Single(subscription.DomainEvents));
        Assert.Equal(old.Id, evt.FromVersionId);
        Assert.Equal(current.Id, evt.ToVersionId);
        Assert.Equal("R1 rebaseline", evt.Reason);
    }

    [Fact]
    public void Rebaselining_to_an_edition_with_other_terms_is_refused()
    {
        var plan = PlanWithEdition(monthly: 49m, yearly: 490m, seats: "3", storage: "1024");
        var old = plan.GetPublishedVersion()!;
        var subscription = ActiveOn(plan, old);
        var cheaper = AddEdition(plan, 39m, 399m, "1", "1024", modules: []);

        var result = subscription.RebaselineVersion(plan, cheaper.Id, "R1 rebaseline", Guid.Empty, DateTime.UtcNow);

        Assert.Equal("Subscription.RebaselineTermsDiffer", result.Error.Code);
        Assert.Equal(old.Id, subscription.PlanVersionId);
        Assert.Empty(subscription.DomainEvents);
    }

    [Fact]
    public void Rebaselining_to_an_edition_of_another_plan_is_refused()
    {
        var plan = PlanWithEdition(monthly: 49m, yearly: 490m, seats: "3", storage: "1024");
        var subscription = ActiveOn(plan, plan.GetPublishedVersion()!);
        var otherPlan = PlanWithEdition(monthly: 49m, yearly: 490m, seats: "3", storage: "1024", code: "other");

        var result = subscription.RebaselineVersion(
            otherPlan,
            otherPlan.GetPublishedVersion()!.Id,
            "R1",
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.Equal("Subscription.RebaselineVersionNotInPlan", result.Error.Code);
    }

    [Fact]
    public void Rebaselining_to_the_edition_it_already_has_does_nothing()
    {
        var plan = PlanWithEdition(monthly: 49m, yearly: 490m, seats: "3", storage: "1024");
        var subscription = ActiveOn(plan, plan.GetPublishedVersion()!);

        var result = subscription.RebaselineVersion(
            plan,
            subscription.PlanVersionId,
            "R1",
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Empty(subscription.DomainEvents);
    }

    private static string? Value(SubscriptionPlanVersion version, string key) =>
        version.Entitlements.FirstOrDefault(entitlement => entitlement.Key.Value == key)?.DefaultValue;

    private static SubscriptionPlan PlanWithEdition(
        decimal monthly,
        decimal yearly,
        string seats,
        string storage,
        string code = "pro"
    )
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Pro, Actor, DateTime.UtcNow)
            .Value;
        var version = Edition(plan, 1, monthly, yearly, seats, storage, modules: ["customers"]);
        Entitlement(
            version,
            CommunicationEntitlementCatalog.MaxRecordingMinutesMeeting,
            EntitlementValueType.Int,
            "60"
        );
        plan.AddVersion(version, Actor, DateTime.UtcNow);
        plan.PublishVersion(version.Id, DateTime.UtcNow, Actor, DateTime.UtcNow);
        return plan;
    }

    private static SubscriptionPlanVersion AddEdition(
        SubscriptionPlan plan,
        decimal monthly,
        decimal yearly,
        string seats,
        string storage,
        string[] modules
    )
    {
        var next = plan.Versions.Max(version => version.VersionNumber) + 1;
        var version = Edition(plan, next, monthly, yearly, seats, storage, ["customers", .. modules]);
        plan.AddVersion(version, Actor, DateTime.UtcNow);
        plan.PublishVersion(version.Id, DateTime.UtcNow, Actor, DateTime.UtcNow);
        return version;
    }

    private static SubscriptionPlanVersion Edition(
        SubscriptionPlan plan,
        int number,
        decimal monthly,
        decimal yearly,
        string seats,
        string storage,
        string[] modules
    )
    {
        var version = SubscriptionPlanVersion
            .Create(plan.Id, number, 14, [BillingCycle.Monthly, BillingCycle.Yearly])
            .Value;
        Entitlement(version, "seats.max", EntitlementValueType.Int, seats);
        Entitlement(version, "storage.max_bytes", EntitlementValueType.Long, storage);
        Entitlement(version, "invitations.max_pending", EntitlementValueType.Int, "5");
        foreach (var module in modules)
            version.AddFeature(
                PlanFeature.Create(version.Id, EntitlementKey.Create($"module.{module}").Value, true, module).Value
            );
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(monthly, "USD").Value).Value
        );
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Yearly, 1, null, Money.Create(yearly, "USD").Value).Value
        );
        return version;
    }

    private static void Entitlement(
        SubscriptionPlanVersion version,
        string key,
        EntitlementValueType type,
        string value
    ) =>
        version.AddEntitlementDefinition(
            PlanEntitlementDefinition.Create(version.Id, EntitlementKey.Create(key).Value, type, value, key).Value
        );

    private static TenantSubscription ActiveOn(SubscriptionPlan plan, SubscriptionPlanVersion version)
    {
        var nowUtc = DateTime.UtcNow;
        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                plan,
                version,
                BillingCycle.Monthly,
                nowUtc,
                nowUtc.AddDays(30),
                Guid.Empty,
                nowUtc
            )
            .Value;
        subscription.ClearDomainEvents();
        return subscription;
    }
}
