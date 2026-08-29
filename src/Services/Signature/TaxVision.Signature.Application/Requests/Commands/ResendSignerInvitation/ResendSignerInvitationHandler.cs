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
        IJtiDenylist denylist,
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

        // Emitir el nuevo token, rotar el jti del firmante y revocar el enlace anterior (si había):
        // el reenvío deja muerto el link viejo sin tocar a los demás firmantes (el epoch es por request).
        var payload = new SigningTokenPayload(
            TenantId: request.TenantId,
            SignatureRequestId: request.Id,
            SignerId: signer.Id,
            RevocationEpoch: request.RevocationEpoch,
            ExpiresAtUtc: request.ExpiresAtUtc,
            TokenId: Guid.NewGuid().ToString("N")
        );
        var token = tokenService.Issue(payload);
        var previousJti = request.RotateSignerToken(signer.Id, payload.TokenId);
        if (previousJti is not null)
            await denylist.RevokeAsync(previousJti, request.ExpiresAtUtc, ct);

        await unitOfWork.SaveChangesAsync(ct);
        await PublishInvitationAsync(request, signer, token, correlation, bus);
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Métodos privados: una responsabilidad cada uno
    // ------------------------------------------------------------------

    private static Task PublishInvitationAsync(
        SignatureRequest request,
        Signer signer,
        string token,
        ICorrelationContext correlation,
        IMessageBus bus
    )
    {
        var evt = new SignerInvitedIntegrationEvent
        {
            TenantId = request.TenantId,
            CorrelationId = correlation.CorrelationId,
            SignatureRequestId = request.Id,
            SignerId = signer.Id,
            Email = signer.Email.Value,
            FullName = signer.FullName.Value,
            Order = signer.Order,
            Language = signer.Language,
            PublicToken = token,
            ExpiresAtUtc = request.ExpiresAtUtc,
            RevocationEpoch = request.RevocationEpoch,
            RequiresConsent = request.RequiresConsent,
            RequiresSequentialSigning = request.RequiresSequentialSigning,
        };
        return bus.PublishAsync(evt).AsTask();
    }
}
