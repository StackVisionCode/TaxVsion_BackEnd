using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Commands;

public sealed record RevertFormTemplateDraftCommand(
    Guid TenantId,
    Guid ByUserId,
    Guid TemplateId,
    int VersionNumber,
    byte[] RowVersion
);

public static class RevertFormTemplateDraftHandler
{
    public static async Task<Result<FormTemplateDetailResponse>> Handle(
        RevertFormTemplateDraftCommand command,
        IFormTemplateRepository templates,
        IFormTemplateVersionRepository versions,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await templates.GetByIdAsync(command.TenantId, command.TemplateId, ct);
        if (found.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(found.Error);
        var concurrency = TemplateApplicationSupport.EnsureVersion(found.Value, command.RowVersion);
        if (concurrency.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(concurrency.Error);
        var version = await versions.GetAsync(command.TenantId, command.TemplateId, command.VersionNumber, ct);
        if (version.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(version.Error);
        var reverted = found.Value.RevertDraftTo(version.Value, command.ByUserId, DateTime.UtcNow);
        if (reverted.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(reverted.Error);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormTemplateDetailResponse.From(found.Value));
    }
}
