using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Attachments.Abstractions;
using TaxVision.Tasks.Application.ClientRequests.Abstractions;
using TaxVision.Tasks.Domain.ClientRequests;
using Wolverine;

namespace TaxVision.Tasks.Application.ClientRequests.Commands;

/// <param name="CustomerId">
/// Del token del portal, nunca de la petición: si lo mandara el cliente, cambiar un id en la llamada
/// bastaría para subir documentos al expediente de otro.
/// </param>
public sealed record SubmitClientDocumentCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid ClientRequestId,
    Guid FileId,
    string? DisplayName,
    string? ContentType,
    long SizeBytes
);

/// <summary>
/// El cliente registra el archivo que acaba de subir a CloudStorage. El pedido pasa a
/// <c>Submitted</c> y el preparador recibe el aviso; nadie da nada por bueno todavía.
/// </summary>
public static class SubmitClientDocumentHandler
{
    public static async Task<Result<PortalClientRequestResponse>> Handle(
        SubmitClientDocumentCommand command,
        IClientRequestRepository requests,
        ITaskFileScanStatusClient files,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var found = await requests.GetByIdAsync(command.TenantId, command.ClientRequestId, ct);
        if (found.IsFailure)
            return Result.Failure<PortalClientRequestResponse>(found.Error);

        var request = found.Value;

        // El pedido de otro cliente responde «no existe»: confirmar que existe ya sería una fuga.
        if (request.CustomerId != command.CustomerId)
            return Result.Failure<PortalClientRequestResponse>(ClientRequestErrors.NotYours);

        var submitted = request.SubmitDocument(
            command.FileId,
            command.DisplayName,
            command.ContentType,
            command.SizeBytes,
            DateTime.UtcNow
        );
        if (submitted.IsFailure)
            return Result.Failure<PortalClientRequestResponse>(submitted.Error);

        // El portal registra el documento cuando el escaneo ya terminó: ese veredicto se publicó antes
        // y no se republica, así que sin preguntarlo el documento quedaba esperando para siempre.
        await ApplyKnownVerdictAsync(request, command.FileId, files, ct);

        await bus.PublishAsync(BuildEvent(request, correlation.CorrelationId));
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(PortalClientRequestResponse.From(request));
    }

    private static async Task ApplyKnownVerdictAsync(
        ClientRequest request,
        Guid fileId,
        ITaskFileScanStatusClient files,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        _ = await files.GetStatusAsync(request.TenantId, fileId, ct) switch
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
            DocumentCount = request.Documents.Count(d => d.IsActive),
        };
}
