using System.Text.Json;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Messaging;

/// <summary>
/// <c>cloudstorage.file.available.v1</c> lo consumen seis servicios y hay envelopes suyos escritos en
/// los outbox durables. Ampliarlo sólo es seguro si un payload viejo —sin los miembros nuevos— sigue
/// deserializando: si alguien los marcara <c>required</c>, todo lo encolado antes del deploy moriría
/// al reintentarse. Este test es lo que impide ese cambio.
/// </summary>
public sealed class FileAvailableContractTests
{
    private const string PayloadBeforeTheOwnerFields = """
        {
          "TenantId": "d4879234-7370-4b58-b49c-094bd7c04847",
          "CorrelationId": "abc-123",
          "FileId": "11111111-1111-1111-1111-111111111111",
          "ObjectKey": "tenants/d487/files/report.pdf",
          "ContentType": "application/pdf",
          "SizeBytes": 20480,
          "ChecksumSha256": "e3b0c44298fc1c149afbf4c8996fb924",
          "CreatedBy": "22222222-2222-2222-2222-222222222222"
        }
        """;

    [Fact]
    public void A_payload_written_before_the_owner_fields_still_deserializes()
    {
        var evt = JsonSerializer.Deserialize<FileAvailableIntegrationEvent>(PayloadBeforeTheOwnerFields);

        Assert.NotNull(evt);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), evt.FileId);
        Assert.Null(evt.OwnerType);
        Assert.Null(evt.OwnerId);
        Assert.Null(evt.FolderId);
    }

    /// <summary>
    /// El consumidor que no conoce los miembros nuevos los ignora; el que sí, los lee. Los dos leen el
    /// mismo mensaje.
    /// </summary>
    [Fact]
    public void The_owner_fields_survive_a_round_trip()
    {
        var original = new FileAvailableIntegrationEvent
        {
            TenantId = Guid.NewGuid(),
            FileId = Guid.NewGuid(),
            ObjectKey = "tenants/x/files/w2.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            ChecksumSha256 = "deadbeef",
            CreatedBy = Guid.NewGuid(),
            OwnerType = "Customer",
            OwnerId = Guid.NewGuid(),
            FolderId = Guid.NewGuid(),
        };

        var round = JsonSerializer.Deserialize<FileAvailableIntegrationEvent>(JsonSerializer.Serialize(original));

        Assert.Equal(original, round);
    }
}
