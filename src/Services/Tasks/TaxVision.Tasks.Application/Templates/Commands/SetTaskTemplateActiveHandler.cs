using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Templates.Abstractions;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Templates;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Templates.Commands;

public sealed record SetTaskTemplateActiveCommand(Guid TenantId, Guid TemplateId, bool IsActive);

public static class SetTaskTemplateActiveHandler
{
    public static async Task<Result> Handle(
        SetTaskTemplateActiveCommand command,
        ITaskTemplateRepository templates,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await templates.GetByIdAsync(command.TenantId, command.TemplateId, ct);
        if (found.IsFailure)
            return Result.Failure(found.Error);

        if (command.IsActive)
            found.Value.Reactivate(DateTime.UtcNow);
        else
            found.Value.Retire(DateTime.UtcNow);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
