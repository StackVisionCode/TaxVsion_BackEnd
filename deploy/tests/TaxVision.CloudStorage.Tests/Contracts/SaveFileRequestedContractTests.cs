using System.Text.Json;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;

namespace TaxVision.CloudStorage.Tests.Contracts;

/// <summary>Un sobre del outbox escrito antes de los campos de origen sigue deserializando.</summary>
public sealed class SaveFileRequestedContractTests
{
    [Fact]
    public void An_event_without_source_fields_still_deserializes()
    {
        var json = $$"""
            {
              "fileId": "{{Guid.NewGuid()}}",
              "requestingService": "signature",
              "sourceBucket": "taxvision-temp",
              "sourceObjectKey": "signature/a.pdf",
              "actorId": "{{Guid.NewGuid()}}",
              "ownerType": "Signature",
              "ownerId": "{{Guid.NewGuid()}}",
              "folderType": "Signatures",
              "originalName": "a.pdf",
              "contentType": "application/pdf",
              "sizeBytes": 10
            }
            """;

        var evt = JsonSerializer.Deserialize<SaveFileRequestedIntegrationEvent>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        )!;

        Assert.Equal("signature", evt.RequestingService);
        Assert.Null(evt.SourceResourceType);
        Assert.Null(evt.SourceResourceId);
        Assert.Null(evt.SourceRole);
        Assert.Null(evt.RelatedCustomerId);
    }
}
