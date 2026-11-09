namespace TaxVision.Catalog.Tests.Architecture;

public sealed class CatalogRoutingTests
{
    private const string SharedFanout = "taxvision-events";
    private const string ItemsExchange = "taxvision-catalog-items";

    [Theory]
    [InlineData("CatalogItemUpdatedIntegrationEvent")]
    [InlineData("CatalogItemPriceChangedIntegrationEvent")]
    public void Updated_and_PriceChanged_are_not_published_to_shared_fanout(string eventName)
    {
        var program = ReadProgram();

        Assert.DoesNotContain(Route(eventName, SharedFanout), program, StringComparison.Ordinal);
        Assert.Contains(Route(eventName, ItemsExchange), program, StringComparison.Ordinal);
    }

    // Inventory los escucha por la cola ligada al fanout: si salen de ahí, deja de enterarse.
    [Theory]
    [InlineData("CatalogItemCreatedIntegrationEvent")]
    [InlineData("CatalogItemDeactivatedIntegrationEvent")]
    [InlineData("CatalogItemTrackingChangedIntegrationEvent")]
    public void Events_with_a_consumer_stay_on_the_shared_fanout(string eventName)
    {
        Assert.Contains(Route(eventName, SharedFanout), ReadProgram(), StringComparison.Ordinal);
    }

    private static string Route(string eventName, string exchange) =>
        $"PublishMessage<{eventName}>().ToRabbitExchange(\"{exchange}\")";

    private static string ReadProgram()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxVision.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(
            Path.Combine(directory!.FullName, "src", "Services", "Catalog", "TaxVision.Catalog.Api", "Program.cs")
        );
    }
}
