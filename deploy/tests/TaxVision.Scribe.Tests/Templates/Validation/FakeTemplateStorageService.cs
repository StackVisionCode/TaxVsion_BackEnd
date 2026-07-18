using BuildingBlocks.Results;
using TaxVision.Scribe.Application.Templates.Storage;

namespace TaxVision.Scribe.Tests.Templates.Validation;

internal sealed class FakeTemplateStorageService : ITemplateStorageService
{
    private readonly Dictionary<Guid, string> _sources = new();

    public void Seed(Guid fileId, string source) => _sources[fileId] = source;

    public Task<Result<TemplateStorageUpload>> UploadAsync(
        Guid? tenantId,
        TemplateArtifactKind kind,
        byte[] content,
        Guid actorId,
        CancellationToken ct = default
    )
    {
        var fileId = Guid.NewGuid();
        _sources[fileId] = System.Text.Encoding.UTF8.GetString(content);
        return Task.FromResult(Result.Success(new TemplateStorageUpload(fileId, $"scribe/{fileId:N}/{kind}")));
    }

    public Task<Result<string>> DownloadTextAsync(Guid fileId, Guid? tenantId, CancellationToken ct = default) =>
        Task.FromResult(
            _sources.TryGetValue(fileId, out var source)
                ? Result.Success(source)
                : Result.Failure<string>(new Error("CloudStorageClient.Download", "File not found."))
        );
}
