using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Public;

public static class RejectSignatureHandler
{
    public static async Task<Result> Handle(
        RejectSignatureCommand cmd,
        ISigningTokenService tokenService,
        ISignatureRequestRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var resolution = await PublicTokenResolver.ResolveAsync(cmd.Token, tokenService, repository, ct);
        if (resolution.IsFailure)
            return Result.Failure(resolution.Error);

        var (request, signer) = (resolution.Value.Request, resolution.Value.Signer);
        var rejectedAt = DateTime.UtcNow;
        var pendingSigners = CollectPendingSignersExcluding(request, signer.Id);

        var rejection = request.MarkSignerRejected(signer.Id, rejectedAt, cmd.Reason, cmd.ClientIp, cmd.UserAgent);
        if (rejection.IsFailure)
            return rejection;

        await unitOfWork.SaveChangesAsync(ct);
        await PublishRejectedAsync(request, signer, rejectedAt, cmd.Reason, pendingSigners, correlation, bus);
        return Result.Success();
    }

    private static IReadOnlyList<Signer> CollectPendingSignersExcluding(SignatureRequest request, Guid excluded) =>
        request.Signers.Where(s => s.Id != excluded && s.Status == SignerStatus.Pending).ToList();

    private static Task PublishRejectedAsync(
        SignatureRequest request,
        Signer signer,
        DateTime rejectedAtUtc,
        string? reason,
        IReadOnlyList<Signer> pendingSigners,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new SignerRejectedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SignatureRequestId = request.Id,
                    CreatedByUserId = request.CreatedByUserId,
                    SignerId = signer.Id,
                    RejectedAtUtc = rejectedAtUtc,
                    RevocationEpoch = request.RevocationEpoch,
                    Reason = reason,
                    PendingSignerIds = pendingSigners.Select(s => s.Id).ToList(),
                    PendingSigners = pendingSigners
                        .Select(s => new SignerContactSnapshot(s.Id, s.Email.Value, s.FullName.Value, "En"))
                        .ToList(),
                }
            )
            .AsTask();
}
