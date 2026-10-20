using System.Text.Json;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Messaging;

public sealed class CallMissedContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Node_payload_maps_to_the_dotnet_contract()
    {
        var evt = JsonSerializer.Deserialize<CallMissedIntegrationEvent>(ReadFixture(), Json);

        Assert.NotNull(evt);
        Assert.Equal(Guid.Parse("6f1c2a3b-1111-4a2b-9c3d-000000000001"), evt.EventId);
        Assert.Equal(Guid.Parse("4c12d117-766d-479c-a7ce-dfb779a1829b"), evt.TenantId);
        Assert.Equal("corr-123", evt.CorrelationId);
        Assert.Equal(Guid.Parse("9b0d2c4e-2222-4b3c-8d4e-000000000002"), evt.CallId);
        Assert.Equal(Guid.Parse("1a2b3c4d-3333-4c4d-9e5f-000000000003"), evt.CallerUserId);
        Assert.Equal(Guid.Parse("7e8f9a0b-4444-4d5e-8f60-000000000004"), evt.CalleeUserId);
        Assert.Equal("Video", evt.Kind);
        Assert.Equal(new DateTime(2026, 10, 17, 15, 3, 35, 123, DateTimeKind.Utc), evt.RingingAtUtc);
        Assert.Equal(new DateTime(2026, 10, 17, 15, 4, 5, 123, DateTimeKind.Utc), evt.MissedAtUtc);
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
                "communication.call.missed.v1.json"
            )
        );
    }
}
