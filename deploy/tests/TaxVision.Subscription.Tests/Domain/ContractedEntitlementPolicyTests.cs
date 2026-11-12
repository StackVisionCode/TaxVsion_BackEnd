using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

// La foto sale de la edición contratada más las reglas de plataforma, nunca de la publicada.
public sealed class ContractedEntitlementPolicyTests
{
    private static readonly Module[] Catalog =
    [
        CoreModule("customers"),
        CoreModule("wallet"),
        CommercialModule("meetings"),
        CommercialModule("email"),
    ];

    [Fact]
    public void Contracted_limits_are_kept_as_signed()
    {
        var version = Version(seatsMax: "25", modules: ["customers"]);

        var entries = ContractedEntitlementPolicy.BaseEntries(version, Catalog, grantsAccess: true).Value;

        Assert.Equal("25", entries["seats.max"].Value);
    }

    [Fact]
    public void A_core_module_missing_from_the_contracted_edition_is_on()
    {
        var version = Version(modules: ["customers"]);

        var entries = ContractedEntitlementPolicy.BaseEntries(version, Catalog, grantsAccess: true).Value;

        Assert.Equal("True", entries["module.wallet"].Value);
        Assert.Equal(EntitlementSource.Plan, entries["module.wallet"].Source);
    }

    [Fact]
    public void A_module_no_longer_in_the_catalog_is_never_granted()
    {
        var version = Version(modules: ["customers", "builder"]);

        var entries = ContractedEntitlementPolicy.BaseEntries(version, Catalog, grantsAccess: true).Value;

        Assert.False(entries.ContainsKey("module.builder"));
    }

    [Fact]
    public void A_commercial_module_comes_only_from_the_contracted_edition()
    {
        var with = ContractedEntitlementPolicy.BaseEntries(Version(modules: ["meetings"]), Catalog, true).Value;
        var without = ContractedEntitlementPolicy.BaseEntries(Version(modules: ["customers"]), Catalog, true).Value;

        Assert.Equal("True", with["module.meetings"].Value);
        Assert.False(without.ContainsKey("module.meetings"));
    }

    [Fact]
    public void A_missing_platform_key_takes_the_catalog_default_and_an_existing_one_is_kept()
    {
        var version = Version(modules: ["customers"]);
        AddEntitlement(version, StorageEntitlementCatalog.MaxFileBytes, EntitlementValueType.Long, "1048576");

        var entries = ContractedEntitlementPolicy.BaseEntries(version, Catalog, grantsAccess: true).Value;

        Assert.Equal("1048576", entries[StorageEntitlementCatalog.MaxFileBytes].Value);
        Assert.Equal("60", entries[CommunicationEntitlementCatalog.MaxRecordingMinutesMeeting].Value);
        Assert.Equal("20", entries[SignatureEntitlementCatalog.MaxDocumentsPerRequest].Value);
    }

    [Fact]
    public void Without_access_every_module_is_off_but_limits_stay()
    {
        var version = Version(seatsMax: "3", modules: ["customers", "meetings"]);

        var entries = ContractedEntitlementPolicy.BaseEntries(version, Catalog, grantsAccess: false).Value;

        Assert.Equal("False", entries["module.customers"].Value);
        Assert.Equal("False", entries["module.wallet"].Value);
        Assert.Equal("False", entries["module.meetings"].Value);
        Assert.Equal(EntitlementStatus.Disabled, entries["module.wallet"].Status);
        Assert.Equal("3", entries["seats.max"].Value);
    }

    [Fact]
    public void Enabled_modules_are_the_contracted_ones_in_the_catalog_plus_every_core_module()
    {
        var version = Version(modules: ["meetings", "builder"]);

        var modules = ContractedEntitlementPolicy.EnabledModules(version, Catalog).Value;

        Assert.Equal(["customers", "meetings", "wallet"], modules);
    }

    // Sin catálogo de módulos no se puede decidir: mejor fallar que apagarle todo al cliente.
    [Fact]
    public void An_empty_module_catalog_fails_instead_of_turning_everything_off()
    {
        var version = Version(modules: ["customers"]);

        Assert.Equal(
            "Entitlements.ModuleCatalogUnavailable",
            ContractedEntitlementPolicy.BaseEntries(version, [], grantsAccess: true).Error.Code
        );
        Assert.Equal(
            "Entitlements.ModuleCatalogUnavailable",
            ContractedEntitlementPolicy.EnabledModules(version, []).Error.Code
        );
    }

    private static SubscriptionPlanVersion Version(string seatsMax = "3", string[]? modules = null)
    {
        var version = SubscriptionPlanVersion.Create(Guid.NewGuid(), 1, 14, [BillingCycle.Monthly]).Value;
        AddEntitlement(version, "seats.max", EntitlementValueType.Int, seatsMax);
        foreach (var module in modules ?? [])
            version.AddFeature(
                PlanFeature.Create(version.Id, EntitlementKey.Create($"module.{module}").Value, true, module).Value
            );
        return version;
    }

    private static void AddEntitlement(
        SubscriptionPlanVersion version,
        string key,
        EntitlementValueType type,
        string value
    ) =>
        version.AddEntitlementDefinition(
            PlanEntitlementDefinition.Create(version.Id, EntitlementKey.Create(key).Value, type, value, key).Value
        );

    private static Module CoreModule(string code) => Module.Create(code, code, code).Value;

    private static Module CommercialModule(string code)
    {
        var module = Module.Create(code, code, code).Value;
        module.MarkAsCommercial();
        return module;
    }
}
