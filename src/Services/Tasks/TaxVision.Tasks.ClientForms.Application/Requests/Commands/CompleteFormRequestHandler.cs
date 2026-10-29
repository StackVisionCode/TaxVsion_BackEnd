using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Reporting;
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
        FormAnswerIndexer indexer,
        SubmissionSummaryRequester summaryRequester,
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
        var outgoing = new DeferredMessages();
        var completed = found.Value.Complete(command.InternalNote, command.ByUserId, command.NowUtc);
        if (completed.IsSuccess)
            await ApplyCompletionAsync(
                found.Value,
                command,
                grants,
                sessions,
                planner,
                indexer,
                summaryRequester,
                outgoing,
                correlation,
                ct
            );
        var saved = await SaveCompletedAsync(completed, found.Value, command, audit, metrics, unitOfWork, ct);
        if (saved.IsSuccess)
        {
            await outgoing.PublishAsync(bus);
            tally.Record(found.Value, metrics);
        }
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

    // Todo lo que sale va a `outgoing` y se publica después de guardar (T-8).
    private static async Task ApplyCompletionAsync(
        FormRequest request,
        CompleteFormRequestCommand command,
        IFormAccessGrantRepository grants,
        IFormAccessSessionRepository sessions,
        FormActionPlanner planner,
        FormAnswerIndexer indexer,
        SubmissionSummaryRequester summaryRequester,
        DeferredMessages outgoing,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        await FormExternalAccessRevoker.RevokeAsync(request, command.NowUtc, grants, sessions, ct);
        outgoing.Add(
            FormRequestIntegrationEventFactory.Completed(request, command.ByUserId, correlation.CorrelationId)
        );
        await planner.PlanAsync(request, FormActionTrigger.OnComplete, null, outgoing, command.NowUtc, ct);
        // Índice de reporting (P-12): se escribe en esta misma transacción, no publica nada.
        await indexer.IndexCompletedAsync(request, command.NowUtc, ct);
        // Submission Summary (P-11): marca la revisión Requested y encola el comando cifrado. Best-effort:
        // si no se pudo armar, la completación igual procede y el staff puede reintentar desde el drawer.
        await summaryRequester.RequestAsync(request, request.CurrentRevisionNumber, outgoing, command.NowUtc, ct);
    }

    private static Task<Result<FormRequestResponse>> SaveCompletedAsync(
        Result completed,
        FormRequest request,
        CompleteFormRequestCommand command,
        IFormAuditLog audit,
        IClientFormsMetrics metrics,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) =>
        ReviewSupport.SaveReviewed(
            completed,
            request,
            "request.completed",
            ClientFormsMetricValues.Completed,
            command.ByUserId,
            command.NowUtc,
            audit,
            metrics,
            unitOfWork,
            ct
        );
}
