using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Attachments.Abstractions;
using TaxVision.Tasks.Application.ClientRequests.Abstractions;
using TaxVision.Tasks.Domain.ClientRequests;
using Wolverine;

namespace TaxVision.Tasks.Application.ClientRequests.Commands;

/// <param name="KnownStatus">Lo que CloudStorage dijo antes de abrir la transacción.</param>
public sealed record RecordClientDocumentCommand(SubmitClientDocumentCommand Document, RemoteFileScanStatus KnownStatus);

/// <summary>TSK-7: la escritura del documento, ya sin llamadas externas.</summary>
public static class RecordClientDocumentHandler
{
    public static async Task<Result<PortalClientRequestResponse>> Handle(
        RecordClientDocumentCommand record,
        IClientRequestRepository requests,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var command = record.Document;
        var found = await ClientDocumentOwnership.CheckAsync(command, requests, ct);
        if (found.IsFailure)
            return Result.Failure<PortalClientRequestResponse>(found.Error);

        var request = found.Value;
        var submitted = request.SubmitDocument(
            command.FileId,
            command.DisplayName,
            command.ContentType,
            command.SizeBytes,
            DateTime.UtcNow
        );
        if (submitted.IsFailure)
            return Result.Failure<PortalClientRequestResponse>(submitted.Error);

        ApplyKnownVerdict(request, command.FileId, record.KnownStatus);

        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(BuildEvent(request, correlation.CorrelationId));

        return Result.Success(PortalClientRequestResponse.From(request));
    }

    private static void ApplyKnownVerdict(ClientRequest request, Guid fileId, RemoteFileScanStatus status)
    {
        var now = DateTime.UtcNow;
        _ = status switch
        {
            RemoteFileScanStatus.Available => request.MarkDocumentAvailable(fileId, now),
            RemoteFileScanStatus.PendingReview => request.MarkDocumentUnderReview(fileId),
            RemoteFileScanStatus.Infected => request.MarkDocumentRejected(fileId, "infected", now),
            RemoteFileScanStatus.BlockedByPolicy => request.MarkDocumentRejected(fileId, "blocked-by-policy", now),
            // Sigue escaneando o no se pudo preguntar: lo resuelve el evento de CloudStorage.
            _ => false,
        };
    }

    private static ClientRequestFulfilledIntegrationEvent BuildEvent(ClientRequest request, string correlationId) =>
        new()
        {
            TenantId = request.TenantId,
            CorrelationId = correlationId,
            ClientRequestId = request.Id,
            CustomerId = request.CustomerId,
            TaskId = request.TaskId,
            Title = request.Title,
            RequestedByUserId = request.RequestedByUserId,
            StaffRecipientUserId = request.StaffRecipientUserId,
            DocumentCount = request.Documents.Count(d => d.IsActive),
        };
}
