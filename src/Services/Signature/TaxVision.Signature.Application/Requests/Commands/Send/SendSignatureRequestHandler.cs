using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Commands.Send;

/// <summary>
/// Transiciona <c>Ready → InProgress</c>. Emite un token público firmado por firmante
/// y publica un <see cref="SignerInvitedIntegrationEvent"/> por cada uno, además del
/// <see cref="SignatureRequestSentIntegrationEvent"/> global. Fases separadas por
/// métodos privados con nombre autoexplicativo — no acumulan responsabilidad.
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
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(cmd.TenantId, cmd.SignatureRequestId, ct);
        if (request is null)
            return NotFound();

        var sentAt = DateTime.UtcNow;
        var transition = request.Send(sentAt);
        if (transition.IsFailure)
            return transition;

        await unitOfWork.SaveChangesAsync(ct);

        var invitations = IssueInvitations(request, tokenService);
        await PublishSentEventAsync(request, sentAt, correlation, bus);
        await PublishInvitationsAsync(request, invitations, correlation, bus);
        return Result.Success();
    }

    private sealed record SignerInvitation(Guid SignerId, string Token, string PublicUrl);

    // ============== Fase A: emitir tokens por firmante ==============

    private static IReadOnlyList<SignerInvitation> IssueInvitations(
        SignatureRequest request,
        ISigningTokenService tokenService
    )
    {
        var invitations = new List<SignerInvitation>(request.Signers.Count);
        foreach (var signer in request.Signers)
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
            invitations.Add(new SignerInvitation(signer.Id, token, tokenService.BuildPublicUrl(token)));
        }
        return invitations;
    }

    // ============== Fase B: publicar Sent global ==============

    private static Task PublishSentEventAsync(
        SignatureRequest request,
        DateTime sentAtUtc,
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
                }
            )
            .AsTask();

    // ============== Fase C: publicar SignerInvited por firmante ==============

    private static async Task PublishInvitationsAsync(
        SignatureRequest request,
        IReadOnlyList<SignerInvitation> invitations,
        ICorrelationContext correlation,
        IMessageBus bus
    )
    {
        foreach (var invitation in invitations)
        {
            var signer = request.Signers.First(s => s.Id == invitation.SignerId);
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
                PublicUrl = invitation.PublicUrl,
                ExpiresAtUtc = request.ExpiresAtUtc,
                RevocationEpoch = request.RevocationEpoch,
                RequiresConsent = request.RequiresConsent,
                RequiresSequentialSigning = request.RequiresSequentialSigning,
            };
            await bus.PublishAsync(evt);
        }
    }

    private static Result NotFound() =>
        Result.Failure(
            new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
        );
}
