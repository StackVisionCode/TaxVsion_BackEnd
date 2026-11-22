using System.Text.Json;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Messaging;

public sealed class InAppNotificationRejectedContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Node_payload_maps_to_the_dotnet_contract()
    {
        var evt = JsonSerializer.Deserialize<InAppNotificationRejectedIntegrationEvent>(ReadFixture(), Json);

        Assert.NotNull(evt);
        Assert.Equal(Guid.Parse("6f1c2a3b-5555-4a2b-9c3d-000000000011"), evt.EventId);
        Assert.Equal(Guid.Parse("4c12d117-766d-479c-a7ce-dfb779a1829b"), evt.TenantId);
        Assert.Equal("corr-456", evt.CorrelationId);
        Assert.Equal(Guid.Parse("9b0d2c4e-6666-4b3c-8d4e-000000000012"), evt.SourceEventId);
        Assert.Equal(Guid.Parse("1a2b3c4d-7777-4c4d-9e5f-000000000013"), evt.RecipientUserId);
        Assert.Equal("tasks.task_overdue", evt.Kind);
        Assert.Equal("InvalidPayload", evt.Reason);
        Assert.Equal("params.title,resourceId", evt.Detail);
    }

    [Fact]
    public void Empty_kind_is_accepted()
    {
        var json = ReadFixture().Replace("\"tasks.task_overdue\"", "\"\"");

        var evt = JsonSerializer.Deserialize<InAppNotificationRejectedIntegrationEvent>(json, Json);

        Assert.NotNull(evt);
        Assert.Equal(string.Empty, evt.Kind);
    }

    private static string ReadFixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxVision.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(
            Path.Combine(
                directory!.FullName,
                "src",
                "BuildingBlocks",
                "Messaging",
                "Contracts",
                "Fixtures",
                "communication.notification.inapp_rejected.v1.json"
            )
        );
    }
}
