using BuildingBlocks.Results;
using TaxVision.Scribe.Application.Abstractions;

namespace TaxVision.Scribe.Tests.Rendering;

internal sealed class FakeCloudStorageClient : ICloudStorageClient
{
    private readonly Dictionary<Guid, string> _sources = new();

    public int DownloadCount { get; private set; }

    public void Seed(Guid fileId, string source) => _sources[fileId] = source;

    public Task<Result<string>> DownloadTextAsync(Guid fileId, Guid? tenantId, CancellationToken ct = default)
    {
        DownloadCount++;
        return Task.FromResult(
            _sources.TryGetValue(fileId, out var source)
                ? Result.Success(source)
                : Result.Failure<string>(new Error("CloudStorageClient.Download", "File not found."))
        );
    }

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
        var storageKey = $"scribe/{fileId:N}/{fileName}";
        _sources[fileId] = System.Text.Encoding.UTF8.GetString(content);
        return Task.FromResult(Result.Success(storageKey));
    }
}
