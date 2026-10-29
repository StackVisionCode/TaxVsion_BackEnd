using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Audit;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Commands;

public sealed record DeleteFormTemplateCommand(Guid TenantId, Guid ByUserId, Guid TemplateId);

public static class DeleteFormTemplateHandler
{
    public static async Task<Result> Handle(
        DeleteFormTemplateCommand command,
        IFormTemplateRepository templates,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await templates.GetByIdAsync(command.TenantId, command.TemplateId, ct);
        if (found.IsFailure)
            return Result.Failure(found.Error);
        var deletable = found.Value.EnsureDeletable();
        if (deletable.IsFailure)
            return deletable;
        templates.Remove(found.Value);
        audit.Record(command.TenantId, found.Value.Id, null, FormAuditAction.TemplateDeleted, command.ByUserId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
