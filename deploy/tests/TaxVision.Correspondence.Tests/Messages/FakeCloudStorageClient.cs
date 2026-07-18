using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Tests.Messages;

internal sealed class FakeCloudStorageClient : ICloudStorageClient
{
    public Result<CloudStorageDownloadUrl> Response { get; set; } =
        Result.Success(
            new CloudStorageDownloadUrl(new Uri("https://storage.example.com/signed"), DateTime.UtcNow.AddMinutes(15))
        );

    public Result<CloudStorageFileMetadata> MetadataResponse { get; set; } =
        Result.Success(new CloudStorageFileMetadata(Guid.Empty, "application/octet-stream", 0));

    public List<(Guid TenantId, Guid FileId)> Calls { get; } = [];

    public List<(Guid TenantId, Guid FileId)> MetadataCalls { get; } = [];

    public Task<Result<CloudStorageDownloadUrl>> GetDownloadUrlAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken ct = default
    )
    {
        Calls.Add((tenantId, fileId));
        return Task.FromResult(Response);
    }

    public Task<Result<CloudStorageFileMetadata>> GetFileMetadataAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken ct = default
    )
    {
        MetadataCalls.Add((tenantId, fileId));
        return Task.FromResult(MetadataResponse);
    }
}
