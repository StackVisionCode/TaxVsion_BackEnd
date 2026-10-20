using System.Text.Json;
using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Messaging;

public sealed class CorrespondenceCustomerEmailReceivedContractTests
{
    private static readonly Guid EventId = Guid.Parse("6f1c2a3b-1111-4a2b-9c3d-000000000001");
    private static readonly Guid TenantId = Guid.Parse("4c12d117-766d-479c-a7ce-dfb779a1829b");

    [Fact]
    public void Golden_payload_matches_the_dotnet_contract()
    {
        var fixture = JsonDocument.Parse(ReadFixture()).RootElement;
        var serialized = JsonDocument.Parse(JsonSerializer.Serialize(BuildEvent())).RootElement;

        Assert.Equal(
            fixture.EnumerateObject().Select(property => property.Name).Order(),
            serialized.EnumerateObject().Select(property => property.Name).Order()
        );
        foreach (var property in fixture.EnumerateObject())
            Assert.Equal(property.Value.ValueKind, serialized.GetProperty(property.Name).ValueKind);
    }

    [Fact]
    public void Payload_without_mailbox_fields_remains_compatible()
    {
        const string legacyPayload = """
            {
              "CustomerId": "1a2b3c4d-3333-4c4d-9e5f-000000000003",
              "IncomingEmailId": "7e8f9a0b-4444-4d5e-8f60-000000000004",
              "EmailThreadId": "9b0d2c4e-2222-4b3c-8d4e-000000000002",
              "Subject": "Quarterly records"
            }
            """;

        var evt = JsonSerializer.Deserialize<CorrespondenceCustomerEmailReceivedIntegrationEvent>(legacyPayload);

        Assert.NotNull(evt);
        Assert.Null(evt.AccountId);
        Assert.Null(evt.MailboxOwnerUserId);
    }

    private static CorrespondenceCustomerEmailReceivedIntegrationEvent BuildEvent() =>
        new()
        {
            EventId = EventId,
            TenantId = TenantId,
            OccurredOn = new DateTime(2026, 10, 17, 15, 4, 5, 123, DateTimeKind.Utc),
            CorrelationId = "corr-123",
            CustomerId = Guid.Parse("1a2b3c4d-3333-4c4d-9e5f-000000000003"),
            IncomingEmailId = Guid.Parse("7e8f9a0b-4444-4d5e-8f60-000000000004"),
            EmailThreadId = Guid.Parse("9b0d2c4e-2222-4b3c-8d4e-000000000002"),
            Subject = "Quarterly records",
            AccountId = Guid.Parse("55bc55cd-5555-4555-8555-000000000005"),
            MailboxOwnerUserId = Guid.Parse("a77d6953-f806-4518-96b7-35734f515e3f"),
        };

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
                "correspondence.customer_email_received.v1.json"
            )
        );
    }
}
