using System.Linq;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class SubscriptionPlanTests
{
    // Análisis 2026-11-11: el borrador nacía vacío y había que reescribir toda la edición para cambiar un valor.
    [Fact]
    public void A_new_draft_starts_as_a_copy_of_the_published_edition()
    {
        var nowUtc = DateTime.UtcNow;
        var plan = PlanTestData.PublishedPlan("gold-2027");
        var published = PlanTestData.PublishNewEdition(plan, 69m, seatsMax: 2);

        var draft = plan.OpenDraft(trialDaysDefault: 30, [], Guid.Empty, nowUtc);

        Assert.True(draft.IsSuccess, draft.IsFailure ? draft.Error.Code : null);
        var version = draft.Value;
        Assert.Equal(PlanVersionStatus.Draft, version.Status);
        Assert.Equal(published.VersionNumber + 1, version.VersionNumber);
        Assert.Equal(30, version.TrialDaysDefault);
        Assert.Equal(published.SupportedBillingCycles, version.SupportedBillingCycles);
        Assert.Equal("2", version.Entitlements.Single(e => e.Key.Value == "seats.max").DefaultValue);
        Assert.Equal(published.Entitlements.Count, version.Entitlements.Count);
        Assert.Equal(published.Features.Count, version.Features.Count);
        Assert.Equal(69m, version.PriceTiers.Single(t => t.BillingCycle == BillingCycle.Monthly).UnitAmount.Amount);
        Assert.All(version.Entitlements, e => Assert.Equal(version.Id, e.PlanVersionId));
    }

    [Fact]
    public void A_plan_without_a_published_edition_opens_an_empty_draft()
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("new-plan").Value, "New", "New plan", PlanTier.Pro, Guid.Empty, DateTime.UtcNow)
            .Value;

        var draft = plan.OpenDraft(trialDaysDefault: 14, [BillingCycle.Monthly], Guid.Empty, DateTime.UtcNow);

        Assert.True(draft.IsSuccess);
        Assert.Empty(draft.Value.Entitlements);
        Assert.Equal(1, draft.Value.VersionNumber);
    }

    // Prueba en vivo E-10: publicar un borrador inválido retiraba la edición publicada y dejaba el plan sin ninguna.
    [Fact]
    public void Publishing_an_invalid_draft_keeps_the_published_edition()
    {
        var plan = PlanTestData.PublishedPlan("gold-2027");
        var published = plan.GetPublishedVersion()!;
        var empty = SubscriptionPlanVersion.Create(plan.Id, 2, 14, [BillingCycle.Monthly]).Value;
        plan.AddVersion(empty, Guid.Empty, DateTime.UtcNow);

        var result = plan.PublishVersion(empty.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("PlanVersion.NoFeatures", result.Error.Code);
        Assert.Equal(published.Id, plan.GetPublishedVersion()?.Id);
        Assert.Equal(PlanVersionStatus.Published, published.Status);
        Assert.Equal(PlanVersionStatus.Draft, empty.Status);
    }

    // D8/F7: una edición publicada siempre trae todas las claves de plataforma (decisión: completar al publicar).
    [Fact]
    public void Publishing_completes_the_platform_keys_without_overwriting_the_ones_the_edition_sets()
    {
        var nowUtc = DateTime.UtcNow;
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("gold-2027").Value, "Gold", "Gold plan", PlanTier.Pro, Guid.Empty, nowUtc)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly]).Value;
        var own = EntitlementKeyCatalog.PlatformDefaults.First(seed => seed.ValueType == EntitlementValueType.Int);
        version.AddEntitlementDefinition(
            PlanEntitlementDefinition
                .Create(version.Id, EntitlementKey.Create(own.Key).Value, EntitlementValueType.Int, "12345", own.Key)
                .Value
        );
        PlanTestData.EnsurePublishableFeature(version);
        PlanTestData.EnsurePublishablePrice(version);
        plan.AddVersion(version, Guid.Empty, nowUtc);

        Assert.True(plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc).IsSuccess);

        Assert.Empty(EntitlementKeyCatalog.MissingFrom(version));
        Assert.Equal("12345", version.Entitlements.Single(e => e.Key.Value == own.Key).DefaultValue);
        Assert.Equal(
            EntitlementKeyCatalog.PlatformDefaults.Count,
            version.Entitlements.Count(e => EntitlementKeyCatalog.PlatformDefaults.Any(seed => seed.Key == e.Key.Value))
        );
    }

    [Fact]
    public void Create_starts_in_draft_status()
    {
        var plan = CreatePlan("starter");

        Assert.Equal(PlanStatus.Draft, plan.Status);
        Assert.Null(plan.GetPublishedVersion());
    }

    [Fact]
    public void Publishing_a_version_moves_the_plan_to_published()
    {
        var plan = CreatePlan("starter");
        var version = CreateSellableVersion(plan.Id, versionNumber: 1);
        plan.AddVersion(version, Guid.Empty, DateTime.UtcNow);

        var result = plan.PublishVersion(version.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanStatus.Published, plan.Status);
        Assert.Equal(version.Id, plan.GetPublishedVersion()!.Id);
    }

    [Fact]
    public void Publishing_a_new_version_supersedes_the_previous_one()
    {
        var plan = CreatePlan("starter");
        var v1 = CreateSellableVersion(plan.Id, versionNumber: 1);
        var v2 = CreateSellableVersion(plan.Id, versionNumber: 2);
        plan.AddVersion(v1, Guid.Empty, DateTime.UtcNow);
        plan.AddVersion(v2, Guid.Empty, DateTime.UtcNow);
        plan.PublishVersion(v1.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);

        var result = plan.PublishVersion(v2.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanVersionStatus.Superseded, v1.Status);
        Assert.Equal(PlanVersionStatus.Published, v2.Status);
        Assert.Equal(v2.Id, plan.GetPublishedVersion()!.Id);
    }

    // Caught in vivo 2026-10-16: una v1 vacía quedó Published tras un DRAFT fallido (400)
    // porque Publish solo validaba Status==Draft. El plan apareció en /subscription/plans sin
    // precio ni módulo — incontratable. El guard pertenece al dominio.
    [Fact]
    public void Publishing_fails_when_version_has_no_features_or_modules()
    {
        var plan = CreatePlan("starter");
        var version = CreateVersion(plan.Id, versionNumber: 1);
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(29m, "USD").Value).Value
        );
        plan.AddVersion(version, Guid.Empty, DateTime.UtcNow);

        var result = plan.PublishVersion(version.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("PlanVersion.NoFeatures", result.Error.Code);
        Assert.Equal(PlanVersionStatus.Draft, version.Status);
    }

    [Fact]
    public void Publishing_fails_when_version_has_no_price_tiers()
    {
        var plan = CreatePlan("starter");
        var version = CreateVersion(plan.Id, versionNumber: 1);
        version.AddFeature(
            PlanFeature.Create(version.Id, EntitlementKey.Create("module.customers").Value, true, "customers").Value
        );
        plan.AddVersion(version, Guid.Empty, DateTime.UtcNow);

        var result = plan.PublishVersion(version.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("PlanVersion.NoPriceTiers", result.Error.Code);
        Assert.Equal(PlanVersionStatus.Draft, version.Status);
    }

    [Fact]
    public void Archiving_a_plan_that_is_not_deprecated_fails()
    {
        var plan = CreatePlan("starter");

        var result = plan.Archive(Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Plan.NotDeprecated", result.Error.Code);
    }

    [Fact]
    public void ReviseModules_publishes_a_new_version_with_the_new_modules_and_keeps_the_rest()
    {
        var plan = CreatePlan("starter");
        var v1 = CreateVersion(plan.Id, versionNumber: 1);
        v1.AddEntitlementDefinition(
            PlanEntitlementDefinition
                .Create(v1.Id, EntitlementKey.Create("seats.max").Value, EntitlementValueType.Int, "5", "seats.max")
                .Value
        );
        v1.AddPriceTier(
            PlanPriceTier.Create(v1.Id, BillingCycle.Monthly, 1, null, Money.Create(10m, "USD").Value).Value
        );
        v1.AddFeature(PlanFeature.Create(v1.Id, EntitlementKey.Create("core.chat").Value, true, "core.chat").Value);
        v1.AddFeature(
            PlanFeature.Create(v1.Id, EntitlementKey.Create("module.signatures").Value, true, "module.signatures").Value
        );
        plan.AddVersion(v1, Guid.Empty, DateTime.UtcNow);
        plan.PublishVersion(v1.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);

        var result = plan.ReviseModules(["documents"], Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanVersionStatus.Superseded, v1.Status);

        var published = plan.GetPublishedVersion()!;
        Assert.NotEqual(v1.Id, published.Id);
        Assert.Equal(2, published.VersionNumber);

        var featureKeys = published.Features.Select(f => f.FeatureKey.Value).ToList();
        Assert.Contains("module.documents", featureKeys);
        Assert.DoesNotContain("module.signatures", featureKeys);
        Assert.Contains("core.chat", featureKeys); // feature no-módulo conservada
        // Límite conservado; el resto son las claves de plataforma que publicar completa.
        Assert.Equal("5", published.Entitlements.Single(e => e.Key.Value == "seats.max").DefaultValue);
        Assert.Single(published.PriceTiers); // precio conservado
    }

    [Fact]
    public void ReviseModules_fails_when_there_is_no_published_version()
    {
        var plan = CreatePlan("starter");

        var result = plan.ReviseModules(["documents"], Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Plan.NoPublishedVersion", result.Error.Code);
    }

    [Fact]
    public void RevisePrices_publishes_a_new_version_with_the_new_prices_and_keeps_modules_and_limits()
    {
        var plan = CreatePlan("starter");
        var v1 = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly, BillingCycle.Yearly]).Value;
        v1.AddEntitlementDefinition(
            PlanEntitlementDefinition
                .Create(v1.Id, EntitlementKey.Create("seats.max").Value, EntitlementValueType.Int, "5", "seats.max")
                .Value
        );
        v1.AddFeature(
            PlanFeature.Create(v1.Id, EntitlementKey.Create("module.signatures").Value, true, "module.signatures").Value
        );
        v1.AddPriceTier(
            PlanPriceTier.Create(v1.Id, BillingCycle.Monthly, 1, null, Money.Create(49m, "USD").Value).Value
        );
        v1.AddPriceTier(
            PlanPriceTier.Create(v1.Id, BillingCycle.Yearly, 1, null, Money.Create(490m, "USD").Value).Value
        );
        plan.AddVersion(v1, Guid.Empty, DateTime.UtcNow);
        plan.PublishVersion(v1.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);

        var result = plan.RevisePrices(59m, 590m, Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanVersionStatus.Superseded, v1.Status);

        var published = plan.GetPublishedVersion()!;
        Assert.Equal(2, published.VersionNumber);
        var monthly = Assert.Single(published.PriceTiers, t => t.BillingCycle == BillingCycle.Monthly);
        var yearly = Assert.Single(published.PriceTiers, t => t.BillingCycle == BillingCycle.Yearly);
        Assert.Equal(59m, monthly.UnitAmount.Amount);
        Assert.Equal(590m, yearly.UnitAmount.Amount);
        Assert.Contains("module.signatures", published.Features.Select(f => f.FeatureKey.Value)); // módulo conservado
        // Límite conservado; el resto son las claves de plataforma que publicar completa.
        Assert.Equal("5", published.Entitlements.Single(e => e.Key.Value == "seats.max").DefaultValue);
    }

    [Fact]
    public void RevisePrices_fails_when_there_is_no_published_version()
    {
        var plan = CreatePlan("starter");

        var result = plan.RevisePrices(59m, 590m, Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Plan.NoPublishedVersion", result.Error.Code);
    }

    private static SubscriptionPlan CreatePlan(string code) =>
        SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Starter, Guid.Empty, DateTime.UtcNow)
            .Value;

    private static SubscriptionPlanVersion CreateVersion(Guid planId, int versionNumber) =>
        SubscriptionPlanVersion.Create(planId, versionNumber, trialDaysDefault: 14, [BillingCycle.Monthly]).Value;

    // Versión mínima publicable: 1 feature + 1 price tier (invariante del dominio).
    private static SubscriptionPlanVersion CreateSellableVersion(Guid planId, int versionNumber)
    {
        var version = CreateVersion(planId, versionNumber);
        version.AddFeature(
            PlanFeature.Create(version.Id, EntitlementKey.Create("module.customers").Value, true, "customers").Value
        );
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(29m, "USD").Value).Value
        );
        return version;
    }
}
