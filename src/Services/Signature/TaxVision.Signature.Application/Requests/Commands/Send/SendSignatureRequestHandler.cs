using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Commands.Send;

/// <summary>
/// Transiciona <c>Ready → InProgress</c> y publica el <see cref="SignatureRequestSentIntegrationEvent"/>.
/// Invita solo a los firmantes del turno: con orden, al primero; sin orden, a todos.
/// </summary>
public static class SendSignatureRequestHandler
{
    public static async Task<Result> Handle(
        SendSignatureRequestCommand cmd,
        ISignatureRequestRepository repository,
        ISigningTokenService tokenService,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISignatureRequestListCacheInvalidator listCache,
        IOfficeTimeZone zones,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(cmd.TenantId, cmd.SignatureRequestId, ct);
        if (request is null)
            return NotFound();

        var sentAt = DateTime.UtcNow;
        // P8: la zona de la oficina queda congelada en la solicitud para el campo Date y el certificado.
        var officeZone = await zones.ResolveAsync(cmd.TenantId, ct);
        var transition = request.Send(sentAt, officeZone.Id);
        if (transition.IsFailure)
            return transition;

        // Emitir los tokens ANTES de guardar: cada emisión registra el jti actual del firmante en el
        // aggregate (para poder revocar ese enlace en un futuro reenvío), y el save lo persiste.
        var invitations = SignerInvitationIssuer.IssueDue(request, tokenService);
        await unitOfWork.SaveChangesAsync(ct);
        await listCache.InvalidateAsync(cmd.TenantId, ct);

        await PublishSentEventAsync(request, sentAt, cmd.SentByUserId, correlation, bus);
        await SignerInvitationIssuer.PublishAsync(request, invitations, correlation, bus);
        return Result.Success();
    }

    // ============== Publicar Sent global ==============

    private static Task PublishSentEventAsync(
        SignatureRequest request,
        DateTime sentAtUtc,
        Guid? sentByUserId,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new SignatureRequestSentIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SignatureRequestId = request.Id,
                    CreatedByUserId = request.CreatedByUserId,
                    SentAtUtc = sentAtUtc,
                    SignerIds = request.Signers.Select(s => s.Id).ToList(),
                    SentByUserId = sentByUserId,
                    CustomerId = request.PrimaryCustomerId,
                }
            )
            .AsTask();

    private static Result NotFound() =>
        Result.Failure(
            new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
        );
}
