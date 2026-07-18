using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class AddOnDefinitionTests
{
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
