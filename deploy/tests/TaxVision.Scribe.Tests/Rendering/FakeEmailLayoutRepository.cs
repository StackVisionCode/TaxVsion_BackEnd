using BuildingBlocks.Results;
using TaxVision.Scribe.Application.Layouts;
using TaxVision.Scribe.Domain.Layouts;

namespace TaxVision.Scribe.Tests.Rendering;

internal sealed class FakeEmailLayoutRepository(EmailLayout layout) : IEmailLayoutRepository
{
    public Task<Result<EmailLayout>> GetByIdAsync(Guid layoutId, CancellationToken ct = default) =>
        Task.FromResult(
            layout.Id == layoutId
                ? Result.Success(layout)
                : Result.Failure<EmailLayout>(new Error("EmailLayout.NotFound", "Not found."))
        );

    public Task AddAsync(EmailLayout value, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<EmailLayout>> GetReferencingFileAsync(Guid fileId, CancellationToken ct = default)
    {
        var referenced = layout.Versions.Any(v =>
            v.HtmlFileId == fileId || v.DesignJsonFileId == fileId || v.PreviewImageFileId == fileId
        );
        return Task.FromResult<IReadOnlyList<EmailLayout>>(referenced ? [layout] : []);
    }
}
