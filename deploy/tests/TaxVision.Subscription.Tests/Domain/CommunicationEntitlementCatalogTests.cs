using System.Linq;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>
/// Las claves communication.* entran al catálogo sin tocar el contrato vigente: el plan gana una
/// versión N+1 con todo lo anterior intacto más las claves que faltaban, y nada más.
/// </summary>
public sealed class CommunicationEntitlementCatalogTests
{
    [Theory]
    [InlineData(PlanTier.Starter, "30")]
    [InlineData(PlanTier.Trial, "30")]
    [InlineData(PlanTier.Pro, "60")]
    [InlineData(PlanTier.Enterprise, "60")]
    public void Only_meeting_recording_minutes_vary_by_tier(PlanTier tier, string expectedMeetingRecordingMinutes)
    {
        var seeds = CommunicationEntitlementCatalog.SeedsFor(tier).ToDictionary(seed => seed.Key);

        Assert.Equal(
            CommunicationEntitlementCatalog.Keys.Order(StringComparer.Ordinal),
            seeds.Keys.Order(StringComparer.Ordinal)
        );
        Assert.Equal(
            expectedMeetingRecordingMinutes,
            seeds[CommunicationEntitlementCatalog.MaxRecordingMinutesMeeting].DefaultValue
        );
        Assert.Equal("10", seeds[CommunicationEntitlementCatalog.MaxParticipantsPerMeeting].DefaultValue);
        Assert.Equal("60", seeds[CommunicationEntitlementCatalog.MaxMeetingMinutes].DefaultValue);
        Assert.Equal("2", seeds[CommunicationEntitlementCatalog.MaxConcurrentCalls].DefaultValue);
        Assert.Equal("600", seeds[CommunicationEntitlementCatalog.MaxMonthlyMinutes].DefaultValue);
        Assert.Equal("60", seeds[CommunicationEntitlementCatalog.MaxCallMinutes].DefaultValue);
        Assert.Equal("60", seeds[CommunicationEntitlementCatalog.MaxRecordingMinutesAudioCall].DefaultValue);
        Assert.Equal("60", seeds[CommunicationEntitlementCatalog.MaxRecordingMinutesVideoCall].DefaultValue);
    }

    [Fact]
    public void Bool_seeds_use_the_same_format_the_recalculation_produces_for_features()
    {
        var seeds = CommunicationEntitlementCatalog.SeedsFor(PlanTier.Starter).ToDictionary(seed => seed.Key);
        string[] flags =
        [
            CommunicationEntitlementCatalog.CallsEnabled,
            CommunicationEntitlementCatalog.SupportChatEnabled,
            CommunicationEntitlementCatalog.RecordingEnabled,
            CommunicationEntitlementCatalog.RecordingVideoEnabled,
            CommunicationEntitlementCatalog.TranscriptEnabled,
        ];

        foreach (var key in flags)
        {
            Assert.Equal(EntitlementValueType.Bool, seeds[key].ValueType);
            Assert.Equal(true.ToString(), seeds[key].DefaultValue);
        }

        Assert.All(
            seeds.Values.Where(seed => !flags.Contains(seed.Key)),
            seed => Assert.Equal(EntitlementValueType.Int, seed.ValueType)
        );
    }

    [Fact]
    public void Every_seed_key_is_a_valid_entitlement_key()
    {
        Assert.All(CommunicationEntitlementCatalog.Keys, key => Assert.True(EntitlementKey.Create(key).IsSuccess));
    }

    [Fact]
    public void Adding_missing_entitlements_publishes_a_new_version_and_keeps_everything_else()
    {
        var plan = CreatePlan(PlanTier.Starter);
        var v1 = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly, BillingCycle.Yearly]).Value;
        v1.AddEntitlementDefinition(
            PlanEntitlementDefinition
                .Create(v1.Id, EntitlementKey.Create("seats.max").Value, EntitlementValueType.Int, "3", "seats.max")
                .Value
        );
        v1.AddFeature(
            PlanFeature.Create(v1.Id, EntitlementKey.Create("module.customers").Value, true, "module.customers").Value
        );
        v1.AddFeature(PlanFeature.Create(v1.Id, EntitlementKey.Create("core.chat").Value, false, "core.chat").Value);
        v1.AddPriceTier(
            PlanPriceTier.Create(v1.Id, BillingCycle.Monthly, 1, null, Money.Create(49m, "USD").Value).Value
        );
        v1.AddPriceTier(
            PlanPriceTier.Create(v1.Id, BillingCycle.Yearly, 1, null, Money.Create(490m, "USD").Value).Value
        );
        plan.AddVersion(v1, Guid.Empty, DateTime.UtcNow);
        plan.PublishVersion(v1.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);

        var result = plan.AddMissingEntitlements(
            CommunicationEntitlementCatalog.SeedsFor(plan.Tier),
            Guid.NewGuid(),
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(CommunicationEntitlementCatalog.Keys.Count, result.Value);
        Assert.Equal(PlanVersionStatus.Superseded, v1.Status);

        var published = plan.GetPublishedVersion()!;
        Assert.NotEqual(v1.Id, published.Id);
        Assert.Equal(2, published.VersionNumber);
        Assert.Equal(14, published.TrialDaysDefault);
        Assert.Equal([BillingCycle.Monthly, BillingCycle.Yearly], published.SupportedBillingCycles);

        var entitlements = published.Entitlements.ToDictionary(e => e.Key.Value);
        Assert.Equal(CommunicationEntitlementCatalog.Keys.Count + 1, entitlements.Count);
        Assert.Equal("3", entitlements["seats.max"].DefaultValue);
        Assert.Equal("30", entitlements[CommunicationEntitlementCatalog.MaxRecordingMinutesMeeting].DefaultValue);
        Assert.Equal("10", entitlements[CommunicationEntitlementCatalog.MaxParticipantsPerMeeting].DefaultValue);
        Assert.All(published.Entitlements, e => Assert.Equal(published.Id, e.PlanVersionId));

        var features = published.Features.ToDictionary(f => f.FeatureKey.Value);
        Assert.Equal(2, features.Count);
        Assert.True(features["module.customers"].DefaultEnabled);
        Assert.False(features["core.chat"].DefaultEnabled);

        var monthly = Assert.Single(published.PriceTiers, t => t.BillingCycle == BillingCycle.Monthly);
        var yearly = Assert.Single(published.PriceTiers, t => t.BillingCycle == BillingCycle.Yearly);
        Assert.Equal(49m, monthly.UnitAmount.Amount);
        Assert.Equal(490m, yearly.UnitAmount.Amount);
        Assert.Equal("USD", yearly.UnitAmount.Currency);
        Assert.NotSame(v1.PriceTiers.First().UnitAmount, monthly.UnitAmount);
    }

    [Fact]
    public void Existing_keys_keep_their_value_and_only_the_missing_ones_are_added()
    {
        var plan = CreatePlan(PlanTier.Pro);
        var v1 = CreateSellableVersion(plan.Id, 1);
        v1.AddEntitlementDefinition(
            PlanEntitlementDefinition
                .Create(
                    v1.Id,
                    EntitlementKey.Create(CommunicationEntitlementCatalog.MaxMeetingMinutes).Value,
                    EntitlementValueType.Int,
                    "120",
                    "custom"
                )
                .Value
        );
        plan.AddVersion(v1, Guid.Empty, DateTime.UtcNow);
        plan.PublishVersion(v1.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);

        var result = plan.AddMissingEntitlements(
            CommunicationEntitlementCatalog.SeedsFor(plan.Tier),
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(CommunicationEntitlementCatalog.Keys.Count - 1, result.Value);
        var published = plan.GetPublishedVersion()!;
        var meetingMinutes = Assert.Single(
            published.Entitlements,
            e => e.Key.Value == CommunicationEntitlementCatalog.MaxMeetingMinutes
        );
        Assert.Equal("120", meetingMinutes.DefaultValue);
        Assert.Equal("custom", meetingMinutes.Description);
        Assert.Equal(
            "60",
            Assert
                .Single(
                    published.Entitlements,
                    e => e.Key.Value == CommunicationEntitlementCatalog.MaxRecordingMinutesMeeting
                )
                .DefaultValue
        );
    }

    [Fact]
    public void Second_call_is_a_no_op_and_does_not_create_a_version()
    {
        var plan = CreatePlan(PlanTier.Enterprise);
        var v1 = CreateSellableVersion(plan.Id, 1);
        plan.AddVersion(v1, Guid.Empty, DateTime.UtcNow);
        plan.PublishVersion(v1.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);
        var seeds = CommunicationEntitlementCatalog.SeedsFor(plan.Tier);
        Assert.True(plan.AddMissingEntitlements(seeds, Guid.Empty, DateTime.UtcNow).IsSuccess);
        var publishedAfterFirstCall = plan.GetPublishedVersion()!;

        var second = plan.AddMissingEntitlements(seeds, Guid.Empty, DateTime.UtcNow);

        Assert.True(second.IsSuccess);
        Assert.Equal(0, second.Value);
        Assert.Equal(2, plan.Versions.Count);
        Assert.Same(publishedAfterFirstCall, plan.GetPublishedVersion());
        Assert.Empty(CommunicationEntitlementCatalog.MissingFrom(publishedAfterFirstCall, plan.Tier));
    }

    [Fact]
    public void Adding_missing_entitlements_fails_when_there_is_no_published_version()
    {
        var plan = CreatePlan(PlanTier.Starter);

        var result = plan.AddMissingEntitlements(
            CommunicationEntitlementCatalog.SeedsFor(plan.Tier),
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Plan.NoPublishedVersion", result.Error.Code);
        Assert.Empty(plan.Versions);
    }

    private static SubscriptionPlan CreatePlan(PlanTier tier) =>
        SubscriptionPlan
            .Create(PlanCode.Create("starter").Value, "Starter", "Starter plan", tier, Guid.Empty, DateTime.UtcNow)
            .Value;

    private static SubscriptionPlanVersion CreateSellableVersion(Guid planId, int versionNumber)
    {
        var version = SubscriptionPlanVersion.Create(planId, versionNumber, 14, [BillingCycle.Monthly]).Value;
        PlanTestData.EnsurePublishableFeature(version);
        PlanTestData.EnsurePublishablePrice(version);
        return version;
    }
}
