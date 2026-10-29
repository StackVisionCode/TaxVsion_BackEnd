using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;
using TaxVision.Tasks.ClientForms.Domain.Audit;

namespace TaxVision.Tasks.ClientForms.Application.Library.Commands;

public sealed record RecordLibraryReviewCommand(
    Guid ByUserId,
    Guid TemplateId,
    int ReviewedVersionNumber,
    string ReviewerRole,
    string? Note
);

public static class RecordLibraryReviewHandler
{
    public static async Task<Result<FormTemplateDetailResponse>> Handle(
        RecordLibraryReviewCommand command,
        IFormTemplateRepository templates,
        IFormTemplateVersionRepository versions,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await templates.GetByIdAsync(PlatformTenant.Id, command.TemplateId, ct);
        if (found.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(found.Error);
        var review = found.Value.RecordLibraryReview(
            command.ReviewedVersionNumber,
            command.ReviewerRole,
            command.ByUserId,
            command.Note,
            PlatformTenant.Id,
            DateTime.UtcNow
        );
        if (review.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(review.Error);
        audit.Record(PlatformTenant.Id, found.Value.Id, null, FormAuditAction.LibraryReviewed, command.ByUserId);
        await unitOfWork.SaveChangesAsync(ct);
        var published = await versions.ListAsync(PlatformTenant.Id, found.Value.Id, ct);
        return Result.Success(
            FormTemplateDetailResponse.From(
                found.Value,
                published.Select(FormTemplateVersionSummaryResponse.From).ToArray()
            )
        );
    }
}
