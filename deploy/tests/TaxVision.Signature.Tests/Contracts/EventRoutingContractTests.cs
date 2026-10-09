namespace TaxVision.Signature.Tests.Contracts;

public sealed class EventRoutingContractTests
{
    private static readonly string[] RequiredPublishedEvents =
    [
        "SignatureDocumentsReadyForSealingIntegrationEvent",
        "SignatureDocumentSealedIntegrationEvent",
        "SignatureRequestSealingCompletedIntegrationEvent",
        "SignatureReadyForDownloadIntegrationEvent",
    ];

    [Fact]
    public void Multi_document_events_are_routed_to_the_shared_exchange()
    {
        var programSource = File.ReadAllText(FindSignatureProgram());

        foreach (var eventName in RequiredPublishedEvents)
        {
            var expectedRoute = $"PublishMessage<{eventName}>().ToRabbitExchange(\"taxvision-events\")";

            Assert.Contains(expectedRoute, programSource, StringComparison.Ordinal);
        }
    }

    private static string FindSignatureProgram()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxVision.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return Path.Combine(
            directory!.FullName,
            "src",
            "Services",
            "Signature",
            "TaxVision.Signature.Api",
            "Program.cs"
        );
    }
}
