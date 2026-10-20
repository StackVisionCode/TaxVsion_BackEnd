using System.Text.Json;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Messaging;

public sealed class InAppNotificationRequestedContractTests
{
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

    // Wolverine aplica JsonNamingPolicy.CamelCase al publicar en RabbitMQ, así que el formato
    // real del cable es camelCase (no las claves PascalCase del fixture dorado, que existe para
    // documentar la forma lógica del contrato). Este test fija ese comportamiento: si alguien
    // configura un serializer que no produce camelCase, el consumer Node (que lee camelCase
    // primero) empezaría a caer al fallback PascalCase sin que nadie lo note.
    [Fact]
    public void Wolverine_serializer_produces_camelCase_on_the_wire()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var serialized = JsonDocument.Parse(JsonSerializer.Serialize(BuildEvent(), options)).RootElement;

        var expected = new[]
        {
            "eventId",
            "tenantId",
            "occurredOn",
            "correlationId",
            "recipientUserId",
            "category",
            "kind",
            "priority",
            "titleKey",
            "bodyKey",
            "params",
            "fallbackTitle",
            "fallbackBody",
            "actorUserId",
            "actorDisplayName",
            "resourceType",
            "resourceId",
            "resourceContext",
            "action",
            "sourceEventId",
            "sourceEventType",
            "groupKey",
            "occurredAtUtc",
        };
        Assert.Equal(
            expected.OrderBy(key => key, StringComparer.Ordinal),
            serialized.EnumerateObject().Select(property => property.Name).OrderBy(key => key, StringComparer.Ordinal)
        );
    }

    private static InAppNotificationRequestedIntegrationEvent BuildEvent() =>
        new()
        {
            EventId = Guid.Parse("6f1c2a3b-1111-4a2b-9c3d-000000000001"),
            TenantId = Guid.Parse("4c12d117-766d-479c-a7ce-dfb779a1829b"),
            OccurredOn = new DateTime(2026, 10, 17, 15, 4, 5, 123, DateTimeKind.Utc),
            CorrelationId = "corr-123",
            RecipientUserId = Guid.Parse("a77d6953-f806-4518-96b7-35734f515e3f"),
            Category = "Work",
            Kind = "tasks.task_overdue",
            Priority = "Normal",
            TitleKey = "notif.tasks.overdue.title",
            BodyKey = "notif.tasks.overdue.body",
            Params = new() { ["taskTitle"] = "File extension", ["clientName"] = "Maria Lopez" },
            FallbackTitle = "Overdue: File extension",
            FallbackBody = "Maria Lopez · due today",
            ResourceType = "task",
            ResourceId = "9b0d2c4e-2222-4b3c-8d4e-000000000002",
            ResourceContext = new() { ["customerId"] = "1a2b3c4d-3333-4c4d-9e5f-000000000003" },
            Action = "open",
            SourceEventId = Guid.Parse("7e8f9a0b-4444-4d5e-8f60-000000000004"),
            SourceEventType = "tasks.task_overdue.v1",
            OccurredAtUtc = new DateTime(2026, 10, 17, 15, 0, 0, DateTimeKind.Utc),
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
                "notification.inapp.requested.v1.json"
            )
        );
    }
}
