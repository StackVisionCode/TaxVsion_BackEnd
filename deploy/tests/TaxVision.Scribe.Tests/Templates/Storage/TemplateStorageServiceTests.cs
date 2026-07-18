using BuildingBlocks.Results;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Application.Templates.Storage;

namespace TaxVision.Scribe.Tests.Templates.Storage;

public sealed class TemplateStorageServiceTests
{
    private sealed class RecordingCloudStorageClient : ICloudStorageClient
    {
        public string? LastFileName { get; private set; }
        public string? LastContentType { get; private set; }
        public string? LastFolderType { get; private set; }
        public Guid? LastTenantId { get; private set; }

        public Task<Result<string>> DownloadTextAsync(Guid fileId, Guid? tenantId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<string>> UploadAsync(
            Guid? tenantId,
            Guid fileId,
            byte[] content,
            string fileName,
            string contentType,
            string folderType,
            Guid actorId,
            CancellationToken ct = default
        )
        {
            LastFileName = fileName;
            LastContentType = contentType;
            LastFolderType = folderType;
            LastTenantId = tenantId;
            return Task.FromResult(Result.Success($"scribe/{fileId:N}/{fileName}"));
        }
    }

    [Theory]
    [InlineData(TemplateArtifactKind.Html, "text/html")]
    [InlineData(TemplateArtifactKind.Text, "text/plain")]
    [InlineData(TemplateArtifactKind.DesignJson, "application/json")]
    [InlineData(TemplateArtifactKind.PreviewImage, "image/png")]
    public async Task UploadAsync_uses_the_correct_content_type_per_artifact_kind(
        TemplateArtifactKind kind,
        string expectedContentType
    )
    {
        var client = new RecordingCloudStorageClient();
        var service = new TemplateStorageService(client);
        var tenantId = Guid.NewGuid();

        var result = await service.UploadAsync(tenantId, kind, [1, 2, 3], Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedContentType, client.LastContentType);
        Assert.Equal("Templates", client.LastFolderType);
        Assert.Equal(tenantId, client.LastTenantId);
    }

    [Fact]
    public async Task UploadAsync_returns_the_storage_key_from_the_cloud_storage_client()
    {
        var client = new RecordingCloudStorageClient();
        var service = new TemplateStorageService(client);

        var result = await service.UploadAsync(null, TemplateArtifactKind.Html, [1], Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.StartsWith("scribe/", result.Value.StorageKey);
        Assert.NotEqual(Guid.Empty, result.Value.FileId);
    }
}
