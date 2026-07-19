using BuildingBlocks.Results;
using TaxVision.Scribe.Application.Abstractions;

namespace TaxVision.Scribe.Application.Templates.Storage;

public sealed class TemplateStorageService(ICloudStorageClient cloudStorageClient) : ITemplateStorageService
{
    private const string TemplatesFolderType = "Templates";

    public async Task<Result<TemplateStorageUpload>> UploadAsync(
        Guid? tenantId,
        TemplateArtifactKind kind,
        byte[] content,
        Guid actorId,
        CancellationToken ct = default
    )
    {
        var fileId = Guid.NewGuid();
        var (fileName, contentType) = DescribeArtifact(kind, fileId);

        var uploadResult = await cloudStorageClient.UploadAsync(
            tenantId,
            fileId,
            content,
            fileName,
            contentType,
            TemplatesFolderType,
            actorId,
            ct
        );

        return uploadResult.IsFailure
            ? Result.Failure<TemplateStorageUpload>(uploadResult.Error)
            : Result.Success(new TemplateStorageUpload(fileId, uploadResult.Value));
    }

    public Task<Result<string>> DownloadTextAsync(Guid fileId, Guid? tenantId, CancellationToken ct = default) =>
        cloudStorageClient.DownloadTextAsync(fileId, tenantId, ct);

    private static (string FileName, string ContentType) DescribeArtifact(TemplateArtifactKind kind, Guid fileId) =>
        kind switch
        {
            TemplateArtifactKind.Html => ($"body-{fileId:N}.html", "text/html"),
            TemplateArtifactKind.Text => ($"body-{fileId:N}.txt", "text/plain"),
            TemplateArtifactKind.DesignJson => ($"design-{fileId:N}.json", "application/json"),
            TemplateArtifactKind.PreviewImage => ($"preview-{fileId:N}.png", "image/png"),
            TemplateArtifactKind.SystemLogo => ($"system-logo-{fileId:N}.png", "image/png"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
}
