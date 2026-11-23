using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Messaging;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Requests;

/// <summary>
/// Invita a los firmantes cuyo turno llegó y aún no tienen enlace. Con orden es uno a la vez; sin orden, todos.
/// Se emite antes de guardar (el jti queda en el aggregate) y se publica después del save.
/// </summary>
public static class SignerInvitationIssuer
{
    public sealed record SignerInvitation(Signer Signer, string Token);

    public static IReadOnlyList<SignerInvitation> IssueDue(SignatureRequest request, ISigningTokenService tokenService)
    {
        var invitations = new List<SignerInvitation>();
        foreach (var signer in request.SignersDueForInvitation())
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
            request.RotateSignerToken(signer.Id, payload.TokenId);
            invitations.Add(new SignerInvitation(signer, token));
        }
        return invitations;
    }

    public static async Task PublishAsync(
        SignatureRequest request,
        IReadOnlyList<SignerInvitation> invitations,
        ICorrelationContext correlation,
        IMessageBus bus
    )
    {
        foreach (var invitation in invitations)
            await bus.PublishAsync(Build(request, invitation.Signer, invitation.Token, correlation));
    }

    public static SignerInvitedIntegrationEvent Build(
        SignatureRequest request,
        Signer signer,
        string token,
        ICorrelationContext correlation
    ) =>
        new()
        {
            TenantId = request.TenantId,
            CorrelationId = correlation.CorrelationId,
            SignatureRequestId = request.Id,
            SignerId = signer.Id,
            Email = signer.Email.Value,
            FullName = signer.FullName.Value,
            Order = signer.Order,
            Language = signer.Language,
            PhoneE164 = signer.PhoneNumber?.Value,
            PreferredChannel = SignerChannelResolver.PreferredChannelFor(signer),
            PublicToken = token,
            ExpiresAtUtc = request.ExpiresAtUtc,
            RevocationEpoch = request.RevocationEpoch,
            RequiresConsent = request.RequiresConsent,
            RequiresSequentialSigning = request.RequiresSequentialSigning,
            MappedCustomerId = signer.MappedCustomerId,
        };
}
