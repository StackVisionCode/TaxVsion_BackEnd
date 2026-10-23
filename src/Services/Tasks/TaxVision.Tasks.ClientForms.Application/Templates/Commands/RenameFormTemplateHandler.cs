using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Commands;

public sealed record RenameFormTemplateCommand(
    Guid TenantId,
    Guid ByUserId,
    Guid TemplateId,
    string? Name,
    string? Description,
    byte[] RowVersion
);

public static class RenameFormTemplateHandler
{
    public static async Task<Result<FormTemplateDetailResponse>> Handle(
        RenameFormTemplateCommand command,
        IFormTemplateRepository templates,
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
        var metadata = TemplateApplicationSupport.Metadata(command.Name, command.Description);
        if (metadata.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(metadata.Error);
        var renamed = found.Value.Rename(
            metadata.Value.Name,
            metadata.Value.Description,
            command.ByUserId,
            DateTime.UtcNow
        );
        if (renamed.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(renamed.Error);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormTemplateDetailResponse.From(found.Value));
    }
}
