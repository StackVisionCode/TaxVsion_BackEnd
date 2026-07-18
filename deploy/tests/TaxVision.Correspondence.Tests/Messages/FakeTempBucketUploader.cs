using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Tests.Messages;

internal sealed class FakeTempBucketUploader : ICorrespondenceTempBucketUploader
{
    public Result<TempBucketUploadResult> Response { get; set; } =
        Result.Success(new TempBucketUploadResult("taxvision-temp", "correspondence/fake/file.pdf"));

    public List<(Guid FileId, byte[] Content, string Filename, string ContentType)> Calls { get; } = [];

    public Task<Result<TempBucketUploadResult>> UploadAsync(
        Guid fileId,
        byte[] content,
        string filename,
        string contentType,
        CancellationToken ct = default
    )
    {
        Calls.Add((fileId, content, filename, contentType));
        return Task.FromResult(Response);
    }
}
