using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;
using TaxVision.Tasks.ClientForms.Domain.Audit;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Commands;

public sealed record RestoreFormTemplateCommand(Guid TenantId, Guid ByUserId, Guid TemplateId, byte[] RowVersion);

public static class RestoreFormTemplateHandler
{
    public static async Task<Result<FormTemplateDetailResponse>> Handle(
        RestoreFormTemplateCommand command,
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
        var restored = found.Value.Restore(command.ByUserId, DateTime.UtcNow);
        if (restored.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(restored.Error);
        audit.Record(command.TenantId, found.Value.Id, null, FormAuditAction.TemplateRestored, command.ByUserId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormTemplateDetailResponse.From(found.Value));
    }
}
