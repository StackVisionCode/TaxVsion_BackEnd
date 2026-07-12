using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Commands.ResendSignerInvitation;

public static class ResendSignerInvitationHandler
{
    public static async Task<Result> Handle(
        ResendSignerInvitationCommand cmd,
        ISignatureRequestRepository repository,
        ISigningTokenService tokenService,
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

        if (request.Status != SignatureRequestStatus.InProgress)
            return Result.Failure(
                new Error("Signature.Request.NotInProgress", "Only InProgress requests can resend invitations.")
            );

        var signer = request.Signers.FirstOrDefault(s => s.Id == cmd.SignerId);
        if (signer is null)
            return Result.Failure(new Error("Signature.Request.SignerMissing", "Signer not found on this request."));

        if (signer.Status != SignerStatus.Pending)
            return Result.Failure(
                new Error("Signature.Signer.NotPending", "Only pending signers can receive a resent invitation.")
            );

        var recordResult = request.RecordReminderDispatched(DateTime.UtcNow);
        if (recordResult.IsFailure)
            return recordResult;

        await unitOfWork.SaveChangesAsync(ct);
        await PublishInvitationAsync(request, signer, tokenService, correlation, bus);
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Métodos privados: una responsabilidad cada uno
    // ------------------------------------------------------------------

    private static Task PublishInvitationAsync(
        SignatureRequest request,
        Signer signer,
        ISigningTokenService tokenService,
        ICorrelationContext correlation,
        IMessageBus bus
    )
    {
        var payload = new SigningTokenPayload(
            TenantId: request.TenantId,
            SignatureRequestId: request.Id,
            SignerId: signer.Id,
            RevocationEpoch: request.RevocationEpoch,
            ExpiresAtUtc: request.ExpiresAtUtc,
            TokenId: Guid.NewGuid().ToString("N")
        );
        var token = tokenService.Issue(payload);
        var publicUrl = tokenService.BuildPublicUrl(token);

        var evt = new SignerInvitedIntegrationEvent
        {
            TenantId = request.TenantId,
            CorrelationId = correlation.CorrelationId,
            SignatureRequestId = request.Id,
            SignerId = signer.Id,
            Email = signer.Email.Value,
            FullName = signer.FullName.Value,
            Order = signer.Order,
            Language = "En",
            PublicUrl = publicUrl,
            ExpiresAtUtc = request.ExpiresAtUtc,
            RevocationEpoch = request.RevocationEpoch,
            RequiresConsent = request.RequiresConsent,
            RequiresSequentialSigning = request.RequiresSequentialSigning,
        };
        return bus.PublishAsync(evt).AsTask();
    }
}
