using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Commands;

// Dos reintentos simultáneos chocan al guardar (rowversion o índice único del intento): solo uno emite.
public static class RetryFormActionRunHandler
{
    public static async Task<Result<FormActionRunResponse>> Handle(
        RetryFormActionRunCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        FormActionPlanner planner,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IClientFormsMetrics metrics,
        CancellationToken ct
    )
    {
        var found = await ReviewSupport.LoadVisible(
            command.TenantId,
            command.ByUserId,
            command.CanViewAll,
            command.RequestId,
            requests,
            visibility,
            ct
        );
        if (found.IsFailure)
            return Result.Failure<FormActionRunResponse>(found.Error);
        var emission = await planner.RequestCustomerCreationAsync(found.Value, command.RunId, command.NowUtc, ct);
        if (emission.IsFailure)
            return Result.Failure<FormActionRunResponse>(emission.Error);
        audit.Record(command.TenantId, found.Value.FormTemplateId, found.Value.Id, "action.retried", command.ByUserId);
        // Guardar antes de publicar: el perdedor de dos reintentos simultáneos no deja un comando suelto (T-8).
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(emission.Value.Message);
        metrics.RecordActionRun(emission.Value.Run.ActionType, emission.Value.Run.Status);
        return Result.Success(FormActionRunResponse.From(emission.Value.Run));
    }
}
