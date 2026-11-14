using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>
/// Claves de plataforma (comunicación, archivos y firma): un solo valor por defecto por clave. Solo rellenan lo
/// que a una edición le falta; lo que ya tiene nunca se pisa.
/// </summary>
public sealed class EntitlementKeyCatalogTests
{
    // Un solo valor por clave, el de Enterprise (25 MB y todos los grupos).
    [Fact]
    public void File_limits_default_to_the_enterprise_policy()
    {
        var seeds = StorageEntitlementCatalog.Defaults.ToDictionary(seed => seed.Key);

        Assert.Equal((25L * 1024 * 1024).ToString(), seeds[StorageEntitlementCatalog.MaxFileBytes].DefaultValue);
        Assert.Equal(EntitlementValueType.Long, seeds[StorageEntitlementCatalog.MaxFileBytes].ValueType);
        Assert.Equal(
            "documents,spreadsheets,images,data,media,presentations,rich_text,web_images,archives,xml",
            seeds[StorageEntitlementCatalog.FileTypeGroups].DefaultValue
        );
    }

    [Fact]
    public void Signature_limits_reproduce_todays_constants()
    {
        {
            var seeds = SignatureEntitlementCatalog.Defaults.ToDictionary(seed => seed.Key, seed => seed.DefaultValue);

            Assert.Equal("20", seeds[SignatureEntitlementCatalog.MaxDocumentsPerRequest]);
            Assert.Equal("50", seeds[SignatureEntitlementCatalog.MaxSignersPerRequest]);
            Assert.Equal((25L * 1024 * 1024).ToString(), seeds[SignatureEntitlementCatalog.MaxPdfBytes]);
            Assert.Equal("100", seeds[SignatureEntitlementCatalog.MaxPagesPerDocument]);
            Assert.Equal("720", seeds[SignatureEntitlementCatalog.MaxTokenHours]);
            Assert.Equal("7", seeds[SignatureEntitlementCatalog.MinRetentionYears]);
            Assert.Equal(false.ToString(), seeds[SignatureEntitlementCatalog.PurgeAllowed]);
            Assert.Equal("Email,Sms", seeds[SignatureEntitlementCatalog.Channels]);
        }
    }

    [Fact]
    public void Every_catalog_key_is_valid_and_unique()
    {
        var keys = EntitlementKeyCatalog.PlatformDefaults.Select(seed => seed.Key).ToList();

        Assert.All(keys, key => Assert.True(EntitlementKey.Create(key).IsSuccess, key));
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            CommunicationEntitlementCatalog.Keys.Count
                + StorageEntitlementCatalog.Keys.Count
                + SignatureEntitlementCatalog.Keys.Count
                + FinancialInsightsEntitlementCatalog.Keys.Count,
            keys.Count
        );
    }

    [Fact]
    public void A_plan_that_already_has_communication_keys_gets_the_new_ones_in_a_single_version()
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("pro").Value, "Pro", "Pro plan", PlanTier.Pro, Guid.Empty, DateTime.UtcNow)
            .Value;
        var v1 = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly]).Value;
        PlanTestData.EnsurePublishableFeature(v1);
        PlanTestData.EnsurePublishablePrice(v1);
        foreach (var seed in CommunicationEntitlementCatalog.Defaults)
            v1.AddEntitlementDefinition(
                PlanEntitlementDefinition
                    .Create(v1.Id, EntitlementKey.Create(seed.Key).Value, seed.ValueType, seed.DefaultValue, seed.Key)
                    .Value
            );
        plan.AddVersion(v1, Guid.Empty, DateTime.UtcNow);
        PlanTestData.PublishAsBeforeF7(plan, v1);

        var nonCommunication =
            StorageEntitlementCatalog.Keys.Count
            + SignatureEntitlementCatalog.Keys.Count
            + FinancialInsightsEntitlementCatalog.Keys.Count;
        Assert.Equal(nonCommunication, EntitlementKeyCatalog.MissingFrom(v1).Count);

        var added = plan.AddMissingEntitlements(EntitlementKeyCatalog.PlatformDefaults, Guid.Empty, DateTime.UtcNow);

        Assert.Equal(nonCommunication, added.Value);
        var published = plan.GetPublishedVersion()!;
        Assert.Equal(2, published.VersionNumber);
        Assert.Empty(EntitlementKeyCatalog.MissingFrom(published));
        Assert.Equal(
            0,
            plan.AddMissingEntitlements(EntitlementKeyCatalog.PlatformDefaults, Guid.Empty, DateTime.UtcNow).Value
        );
    }
}
