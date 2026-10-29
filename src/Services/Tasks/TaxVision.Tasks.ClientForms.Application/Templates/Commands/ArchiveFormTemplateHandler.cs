using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;
using TaxVision.Tasks.ClientForms.Domain.Audit;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Commands;

public sealed record ArchiveFormTemplateCommand(Guid TenantId, Guid ByUserId, Guid TemplateId, byte[] RowVersion);

public static class ArchiveFormTemplateHandler
{
    public static async Task<Result<FormTemplateDetailResponse>> Handle(
        ArchiveFormTemplateCommand command,
        IFormTemplateRepository templates,
        IFormAuditLog audit,
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
        var archived = found.Value.Archive(command.ByUserId, DateTime.UtcNow);
        if (archived.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(archived.Error);
        audit.Record(command.TenantId, found.Value.Id, null, FormAuditAction.TemplateArchived, command.ByUserId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormTemplateDetailResponse.From(found.Value));
    }
}
