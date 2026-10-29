using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Commands;

// Arranque manual del alta, o reintento del último intento si su resultado lo admite.
public static class StartFormCustomerCreationHandler
{
    public static async Task<Result<FormActionRunResponse>> Handle(
        StartFormCustomerCreationCommand command,
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
        var emission = await planner.RequestCustomerCreationAsync(found.Value, null, command.NowUtc, ct);
        if (emission.IsFailure)
            return Result.Failure<FormActionRunResponse>(emission.Error);
        audit.Record(
            command.TenantId,
            found.Value.FormTemplateId,
            found.Value.Id,
            "action.requested",
            command.ByUserId
        );
        // Guardar antes de publicar: un conflicto no deja un comando suelto (T-8).
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(emission.Value.Message);
        metrics.RecordActionRun(emission.Value.Run.ActionType, emission.Value.Run.Status);
        return Result.Success(FormActionRunResponse.From(emission.Value.Run));
    }
}
