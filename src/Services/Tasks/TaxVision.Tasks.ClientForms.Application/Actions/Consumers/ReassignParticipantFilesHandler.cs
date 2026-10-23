using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Actions.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Consumers;

/// <summary>
/// Pasa los adjuntos del externo al cliente vinculado por el endpoint interno de CloudStorage. Un fallo
/// HTTP lanza para que el bus reintente; un OwnerMismatch o LegalHold queda registrado y no se reintenta.
/// </summary>
public static class ReassignParticipantFilesHandler
{
    private const int MaxFilesPerCall = 50;

    public static async Task Handle(
        ReassignParticipantFilesRequested message,
        IFormRequestRepository requests,
        IFormFileReassignmentClient files,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<ReassignParticipantFilesRequested> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(FormActionResultSupport.CorrelationOf(message.CorrelationId, message.FormRequestId)))
        {
            var request = await FormActionResultSupport.LoadAsync(
                message.TenantId,
                message.FormRequestId,
                requests,
                logger,
                ct
            );
            if (request is not { CustomerLinkStatus: CustomerLinkStatus.Linked })
                return;
            var pending = request.FilesPendingOwnerReassignment();
            if (pending.Count == 0)
                return;
            var blocked = false;
            foreach (var chunk in pending.Chunk(MaxFilesPerCall))
                blocked |= await ReassignChunkAsync(request, chunk, files, ct);
            if (blocked)
                await bus.PublishAsync(
                    CustomerCreationCommandFactory.Failed(
                        request,
                        FormActionNames.CreateCustomer,
                        FormActionNames.FilesNotReassignedCode,
                        correlation.CorrelationId
                    )
                );
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    // True si algún archivo quedó con un dueño o una retención que impiden pasarlo al cliente.
    private static async Task<bool> ReassignChunkAsync(
        FormRequest request,
        IReadOnlyList<Guid> chunk,
        IFormFileReassignmentClient files,
        CancellationToken ct
    )
    {
        var result = await files.ReassignAsync(
            new FormFileReassignmentRequest(
                request.TenantId,
                request.Id,
                request.ParticipantId!.Value,
                request.CustomerId!.Value,
                chunk
            ),
            ct
        );
        if (result.IsFailure)
            throw new InvalidOperationException($"File reassignment is unavailable: {result.Error.Code}.");
        var blocked = false;
        foreach (var file in result.Value)
        {
            request.RecordFileReassignment(file.FileId, file.Outcome, DateTime.UtcNow);
            blocked |=
                file.Outcome is FormFileReassignmentOutcomes.OwnerMismatch or FormFileReassignmentOutcomes.LegalHold;
        }
        return blocked;
    }
}
