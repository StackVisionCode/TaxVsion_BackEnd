namespace TaxVision.CloudStorage.Tests.Contracts;

public sealed class EventRoutingContractTests
{
    [Fact]
    public void FileScanFailed_is_routed_to_the_shared_exchange()
    {
        var programSource = File.ReadAllText(FindProgram());

        Assert.Contains(
            "PublishMessage<FileScanFailedIntegrationEvent>().ToRabbitExchange(\"taxvision-events\")",
            programSource,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void FileDeletionSkipped_is_routed_to_the_shared_exchange()
    {
        var programSource = File.ReadAllText(FindProgram());

        Assert.Contains(
            "PublishMessage<FileDeletionSkippedIntegrationEvent>().ToRabbitExchange(\"taxvision-events\")",
            programSource,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void File_deletions_command_queue_is_listened_with_a_durable_inbox()
    {
        var programSource = File.ReadAllText(FindProgram());

        Assert.Contains(
            "ListenToRabbitQueue(\"cloudstorage-file-deletions\").UseDurableInbox()",
            programSource,
            StringComparison.Ordinal
        );
    }

    private static string FindProgram()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxVision.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(
            directory!.FullName,
            "src",
            "Services",
            "CloudStorage",
            "TaxVision.CloudStorage.Api",
            "Program.cs"
        );
    }
}
