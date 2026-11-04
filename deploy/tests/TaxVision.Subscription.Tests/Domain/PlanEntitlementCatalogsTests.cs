using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>
/// Los límites de archivos y de firma pasan a ser del plan con los valores que hoy estaban fijos en
/// CloudStorage y Signature: desplegar no cambia nada; desde ahí, se editan en la versión del plan.
/// </summary>
public sealed class PlanEntitlementCatalogsTests
{
    [Theory]
    [InlineData(PlanTier.Starter, 10L * 1024 * 1024, "documents,spreadsheets,images,data,media")]
    [InlineData(PlanTier.Trial, 10L * 1024 * 1024, "documents,spreadsheets,images,data,media")]
    [InlineData(
        PlanTier.Pro,
        25L * 1024 * 1024,
        "documents,spreadsheets,images,data,media,presentations,rich_text,web_images,archives,xml"
    )]
    [InlineData(
        PlanTier.Enterprise,
        25L * 1024 * 1024,
        "documents,spreadsheets,images,data,media,presentations,rich_text,web_images,archives,xml"
    )]
    public void File_limits_reproduce_the_per_plan_policies_that_lived_in_cloudstorage_appsettings(
        PlanTier tier,
        long expectedMaxFileBytes,
        string expectedGroups
    )
    {
        var seeds = StorageEntitlementCatalog.SeedsFor(tier).ToDictionary(seed => seed.Key);

        Assert.Equal(expectedMaxFileBytes.ToString(), seeds[StorageEntitlementCatalog.MaxFileBytes].DefaultValue);
        Assert.Equal(EntitlementValueType.Long, seeds[StorageEntitlementCatalog.MaxFileBytes].ValueType);
        Assert.Equal(expectedGroups, seeds[StorageEntitlementCatalog.FileTypeGroups].DefaultValue);
    }

    [Fact]
    public void Signature_limits_reproduce_todays_constants_for_every_plan()
    {
        foreach (var tier in Enum.GetValues<PlanTier>())
        {
            var seeds = SignatureEntitlementCatalog
                .SeedsFor(tier)
                .ToDictionary(seed => seed.Key, seed => seed.DefaultValue);

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
        var keys = PlanEntitlementCatalogs.SeedsFor(PlanTier.Pro).Select(seed => seed.Key).ToList();

        Assert.All(keys, key => Assert.True(EntitlementKey.Create(key).IsSuccess, key));
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            CommunicationEntitlementCatalog.Keys.Count
                + StorageEntitlementCatalog.Keys.Count
                + SignatureEntitlementCatalog.Keys.Count,
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
        foreach (var seed in CommunicationEntitlementCatalog.SeedsFor(PlanTier.Pro))
            v1.AddEntitlementDefinition(
                PlanEntitlementDefinition
                    .Create(v1.Id, EntitlementKey.Create(seed.Key).Value, seed.ValueType, seed.DefaultValue, seed.Key)
                    .Value
            );
        plan.AddVersion(v1, Guid.Empty, DateTime.UtcNow);
        plan.PublishVersion(v1.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);

        Assert.Equal(
            StorageEntitlementCatalog.Keys.Count + SignatureEntitlementCatalog.Keys.Count,
            PlanEntitlementCatalogs.MissingFrom(v1, PlanTier.Pro).Count
        );

        var added = plan.AddMissingEntitlements(
            PlanEntitlementCatalogs.SeedsFor(PlanTier.Pro),
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.Equal(StorageEntitlementCatalog.Keys.Count + SignatureEntitlementCatalog.Keys.Count, added.Value);
        var published = plan.GetPublishedVersion()!;
        Assert.Equal(2, published.VersionNumber);
        Assert.Empty(PlanEntitlementCatalogs.MissingFrom(published, PlanTier.Pro));
        Assert.Equal(
            0,
            plan.AddMissingEntitlements(
                PlanEntitlementCatalogs.SeedsFor(PlanTier.Pro),
                Guid.Empty,
                DateTime.UtcNow
            ).Value
        );
    }
}
