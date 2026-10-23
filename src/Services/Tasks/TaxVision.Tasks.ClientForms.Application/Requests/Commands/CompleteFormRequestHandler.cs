using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Commands;

public static class CompleteFormRequestHandler
{
    public static async Task<Result<FormRequestResponse>> Handle(
        CompleteFormRequestCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        IFormAccessGrantRepository grants,
        IFormAccessSessionRepository sessions,
        FormActionPlanner planner,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        IClientFormsMetrics metrics,
        CancellationToken ct
    )
    {
        var found = await LoadWritableAsync(command, requests, visibility, ct);
        if (found.IsFailure)
            return Result.Failure<FormRequestResponse>(found.Error);
        var tally = FormActionRunTally.Of(found.Value);
        var completed = found.Value.Complete(command.InternalNote, command.ByUserId, command.NowUtc);
        if (completed.IsSuccess)
        {
            await CloseAccessAndAnnounceAsync(found.Value, command, grants, sessions, bus, correlation, ct);
            await planner.PlanAsync(found.Value, FormActionTrigger.OnComplete, null, bus, command.NowUtc, ct);
        }
        var saved = await ReviewSupport.SaveReviewed(
            completed,
            found.Value,
            "request.completed",
            ClientFormsMetricValues.Completed,
            command.ByUserId,
            command.NowUtc,
            audit,
            metrics,
            unitOfWork,
            ct
        );
        if (saved.IsSuccess)
            tally.Record(found.Value, metrics);
        return saved;
    }

    private static async Task<Result<FormRequest>> LoadWritableAsync(
        CompleteFormRequestCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
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
            return found;
        var writable = ReviewSupport.EnsureWritable(found.Value, command.RowVersion, command.InternalNote);
        return writable.IsFailure ? Result.Failure<FormRequest>(writable.Error) : found;
    }

    private static async Task CloseAccessAndAnnounceAsync(
        FormRequest request,
        CompleteFormRequestCommand command,
        IFormAccessGrantRepository grants,
        IFormAccessSessionRepository sessions,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        await FormExternalAccessRevoker.RevokeAsync(request, command.NowUtc, grants, sessions, ct);
        await bus.PublishAsync(
            FormRequestIntegrationEventFactory.Completed(request, command.ByUserId, correlation.CorrelationId)
        );
    }
}
