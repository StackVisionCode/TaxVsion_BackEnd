using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Commands.Cancel;

public static class CancelSignatureRequestHandler
{
    public static async Task<Result> Handle(
        CancelSignatureRequestCommand cmd,
        ISignatureRequestRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(cmd.TenantId, cmd.SignatureRequestId, ct);
        if (request is null)
            return NotFound();

        var pendingSignerIds = CollectPendingSignerIds(request);
        var canceledAt = DateTime.UtcNow;
        var transition = request.Cancel(canceledAt);
        if (transition.IsFailure)
            return transition;

        await unitOfWork.SaveChangesAsync(ct);
        await PublishCanceledEventAsync(request, cmd, canceledAt, pendingSignerIds, correlation, bus);
        return Result.Success();
    }

    private static Result NotFound() =>
        Result.Failure(
            new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
        );

    private static IReadOnlyList<Guid> CollectPendingSignerIds(SignatureRequest request) =>
        request.Signers.Where(s => s.Status == SignerStatus.Pending).Select(s => s.Id).ToList();

    private static Task PublishCanceledEventAsync(
        SignatureRequest request,
        CancelSignatureRequestCommand cmd,
        DateTime canceledAtUtc,
        IReadOnlyList<Guid> pendingSignerIds,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new SignatureRequestCanceledIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SignatureRequestId = request.Id,
                    CreatedByUserId = request.CreatedByUserId,
                    CanceledByUserId = cmd.CanceledByUserId,
                    CanceledAtUtc = canceledAtUtc,
                    Reason = cmd.Reason,
                    PendingSignerIds = pendingSignerIds,
                }
            )
            .AsTask();
}
