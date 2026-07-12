using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Commands.ExtendExpiration;

public static class ExtendExpirationHandler
{
    public static async Task<Result> Handle(
        ExtendExpirationCommand cmd,
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

        var transition = request.ExtendExpiration(cmd.AdditionalHours);
        if (transition.IsFailure)
            return transition;

        await unitOfWork.SaveChangesAsync(ct);
        await PublishExtendedEventAsync(request, cmd, correlation, bus);
        return Result.Success();
    }

    private static Result NotFound() =>
        Result.Failure(
            new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
        );

    private static Task PublishExtendedEventAsync(
        SignatureRequest request,
        ExtendExpirationCommand cmd,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new SignatureRequestExpirationExtendedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SignatureRequestId = request.Id,
                    ExtendedByUserId = cmd.ExtendedByUserId,
                    AdditionalHours = cmd.AdditionalHours,
                    NewExpiresAtUtc = request.ExpiresAtUtc,
                    RevocationEpoch = request.RevocationEpoch,
                }
            )
            .AsTask();
}
