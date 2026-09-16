using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class AddOnDefinitionTests
{
    [Fact]
    public void IsAbsorbedByPlanModules_true_when_the_plan_covers_all_its_modules()
    {
        var def = ModuleAddOn("module.email");

        Assert.True(def.IsAbsorbedByPlanModules(["module.email", "module.comms"]));
    }

    [Fact]
    public void IsAbsorbedByPlanModules_false_when_a_module_is_not_covered()
    {
        var def = ModuleAddOn("module.email");

        Assert.False(def.IsAbsorbedByPlanModules(["module.comms"]));
    }

    [Fact]
    public void IsAbsorbedByPlanModules_false_for_a_quota_add_on_even_if_its_module_is_covered()
    {
        var def = ModuleAddOn("module.email");
        def.AddEntitlementDefinition(
            AddOnEntitlementDefinition
                .Create(
                    def.Id,
                    EntitlementKey.Create("storage.max_bytes").Value,
                    EntitlementValueType.Long,
                    "100",
                    AddOnMergeStrategy.Sum
                )
                .Value
        );

        Assert.False(def.IsAbsorbedByPlanModules(["module.email"]));
    }

    [Fact]
    public void IsAbsorbedByPlanModules_false_when_it_has_no_module_feature()
    {
        var def = CreateDefinition();
        def.AddEntitlementDefinition(
            AddOnEntitlementDefinition
                .Create(
                    def.Id,
                    EntitlementKey.Create("storage.max_bytes").Value,
                    EntitlementValueType.Long,
                    "100",
                    AddOnMergeStrategy.Max
                )
                .Value
        );

        Assert.False(def.IsAbsorbedByPlanModules(["module.email"]));
    }

    private static AddOnDefinition ModuleAddOn(string moduleKey)
    {
        var def = CreateDefinition();
        def.AddFeature(AddOnFeature.Create(def.Id, EntitlementKey.Create(moduleKey).Value, enabled: true).Value);
        return def;
    }

    [Fact]
    public void Create_starts_in_draft_status()
    {
        var definition = CreateDefinition();

        Assert.Equal(AddOnDefinitionStatus.Draft, definition.Status);
    }

    [Fact]
    public void Publish_moves_a_draft_definition_to_published()
    {
        var definition = CreateDefinition();

        var result = definition.Publish(Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(AddOnDefinitionStatus.Published, definition.Status);
    }

    [Fact]
    public void Publish_a_published_definition_fails()
    {
        var definition = CreateDefinition();
        definition.Publish(Guid.Empty, DateTime.UtcNow);

        var result = definition.Publish(Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("AddOnDefinition.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void Archive_requires_deprecated_status()
    {
        var definition = CreateDefinition();
        definition.Publish(Guid.Empty, DateTime.UtcNow);

        var result = definition.Archive(Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("AddOnDefinition.NotDeprecated", result.Error.Code);
    }

    [Fact]
    public void Seed_uses_the_given_id()
    {
        var id = Guid.NewGuid();

        var definition = AddOnDefinition
            .Seed(
                id,
                AddOnCode.Create("addon-email").Value,
                "Correo",
                "Modulo Correo",
                "module",
                false,
                [BillingCycle.Monthly],
                DateTime.UtcNow
            )
            .Value;

        Assert.Equal(id, definition.Id);
    }

    [Fact]
    public void ResolveUnitPrice_returns_the_amount_of_the_matching_tier()
    {
        var definition = CreateDefinition();
        definition.AddPriceTier(
            AddOnPriceTier.Create(definition.Id, BillingCycle.Monthly, 1, null, Money.Create(29m, "USD").Value).Value
        );

        var price = definition.ResolveUnitPrice(BillingCycle.Monthly, 1);

        Assert.True(price.IsSuccess);
        Assert.Equal(29m, price.Value.Amount);
        Assert.Equal("USD", price.Value.Currency);
    }

    [Fact]
    public void ResolveUnitPrice_fails_when_no_tier_matches_the_cycle()
    {
        var definition = CreateDefinition();
        definition.AddPriceTier(
            AddOnPriceTier.Create(definition.Id, BillingCycle.Monthly, 1, null, Money.Create(29m, "USD").Value).Value
        );

        var price = definition.ResolveUnitPrice(BillingCycle.Yearly, 1);

        Assert.True(price.IsFailure);
        Assert.Equal("AddOn.NoPriceTier", price.Error.Code);
    }

    private static AddOnDefinition CreateDefinition() =>
        AddOnDefinition
            .Create(
                AddOnCode.Create("storage.extra_100gb").Value,
                "Extra storage",
                "100GB of additional storage",
                "storage",
                allowMultipleInstances: true,
                [BillingCycle.Monthly],
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
}
