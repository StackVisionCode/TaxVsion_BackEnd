using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Commands.SignAsPreparer;

public sealed record SignAsPreparerCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    Guid PreparerUserId,
    string? ClientIp,
    string? UserAgent
);

public static class SignAsPreparerHandler
{
    public static async Task<Result> Handle(
        SignAsPreparerCommand cmd,
        ISignatureRequestRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(cmd.TenantId, cmd.SignatureRequestId, ct);
        if (request is null)
            return Result.Failure(
                new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
            );

        var signedAt = DateTime.UtcNow;
        var signResult = request.MarkPreparerSigned(cmd.PreparerUserId, signedAt, cmd.ClientIp, cmd.UserAgent);
        if (signResult.IsFailure)
            return signResult;

        await unitOfWork.SaveChangesAsync(ct);
        await PublishPreparerSignedAsync(request, signedAt, correlation, bus);
        return Result.Success();
    }

    private static Task PublishPreparerSignedAsync(
        SignatureRequest request,
        DateTime signedAtUtc,
        ICorrelationContext correlation,
        IMessageBus bus
    )
    {
        var preparer = request.Preparer!;
        return bus.PublishAsync(
                new PreparerSignedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SignatureRequestId = request.Id,
                    CreatedByUserId = request.CreatedByUserId,
                    PreparerUserId = request.PreparerSignedByUserId!.Value,
                    PtinOrEfin = preparer.PtinOrEfin,
                    PreparerDisplayName = preparer.DisplayName,
                    SignedAtUtc = signedAtUtc,
                }
            )
            .AsTask();
    }
}
