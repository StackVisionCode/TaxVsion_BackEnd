using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Commands;

/// <summary>
/// Reintenta el Submission Summary de una revisión completada: arma de nuevo el snapshot, marca la revisión
/// Requested con un GenerationId nuevo y reenvía el comando cifrado. Guardar antes de publicar: el requester
/// publica por el outbox y acá se confirma en una sola transacción.
/// </summary>
public static class RetrySubmissionSummaryHandler
{
    public static async Task<Result<FormRequestResponse>> Handle(
        RetrySubmissionSummaryCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        SubmissionSummaryRequester summaryRequester,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var found = await LoadCompletedAsync(command, requests, visibility, ct);
        if (found.IsFailure)
            return Result.Failure<FormRequestResponse>(found.Error);

        var request = found.Value;
        var outgoing = new DeferredMessages();
        var requested = await summaryRequester.RequestAsync(
            request,
            request.CurrentRevisionNumber,
            outgoing,
            command.NowUtc,
            ct
        );
        if (requested.IsFailure)
            return Result.Failure<FormRequestResponse>(requested.Error);

        audit.Record(command.TenantId, request.FormTemplateId, request.Id, "summary.retried", command.ByUserId);
        // Guardar antes de publicar (T-8).
        await unitOfWork.SaveChangesAsync(ct);
        await outgoing.PublishAsync(bus);
        return Result.Success(FormRequestResponse.From(request));
    }

    // Solo una request completada con al menos una revisión admite reintento.
    private static async Task<Result<FormRequest>> LoadCompletedAsync(
        RetrySubmissionSummaryCommand command,
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

        var request = found.Value;
        if (request.Status != FormRequestStatus.Completed || request.CurrentRevisionNumber < 1)
            return Result.Failure<FormRequest>(FormRequestErrors.InvalidTransition);
        return found;
    }
}
