using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Statuses.Abstractions;
using TaxVision.Tasks.Domain.Statuses;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Statuses.Commands;

public sealed record RestoreWorkflowStatusCommand(Guid TenantId, Guid ByUserId, string Code);

public static class RestoreWorkflowStatusHandler
{
    public static async Task<Result<WorkflowStatusResponse>> Handle(
        RestoreWorkflowStatusCommand command,
        IWorkflowStatusRepository statuses,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var code = TaskLabelCode.Create(command.Code);
        var status = code.IsSuccess ? await statuses.GetByCodeAsync(command.TenantId, code.Value, ct) : null;
        if (status is null)
            return Result.Failure<WorkflowStatusResponse>(TaskErrors.Status.NotFound);
        if (await statuses.CountActiveAsync(command.TenantId, ct) >= WorkflowStatus.MaxActivePerTenant)
            return Result.Failure<WorkflowStatusResponse>(TaskErrors.Status.LimitReached);
        if (await statuses.NameTakenAsync(command.TenantId, status.DisplayName, status.Id, ct))
            return Result.Failure<WorkflowStatusResponse>(TaskErrors.Status.NameTaken);

        status.Restore(command.ByUserId, DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(WorkflowStatusResponse.From(status));
    }
}
