using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

internal static class PublicDirectedFormAccess
{
    internal sealed record Context(FormAccessGrant Grant, FormRequest Request, FormParticipant Participant);

    internal static async Task<Context?> LoadAsync(
        string accessToken,
        string requestHost,
        DateTime nowUtc,
        IFormAccessGrantRepository grants,
        IFormRequestRepository requests,
        IFormParticipantRepository participants,
        IFormExternalAccessSecurity security,
        IFormPublicTenantHostValidator hostValidator,
        IClientFormsPublicCapabilityEvaluator capabilities,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(requestHost))
            return null;
        var grant = await grants.GetByTokenHashAsync(security.HashToken(accessToken), ct);
        if (grant is null || !grant.IsAvailableAt(nowUtc))
            return null;
        if (!await hostValidator.MatchesTenantAsync(grant.TenantId, requestHost, ct))
            return null;
        if (!await capabilities.IsExternalParticipantAvailableAsync(grant.TenantId, ct))
            return null;

        var request = await requests.GetForCommandAsync(grant.TenantId, grant.FormRequestId, ct);
        // Una request cerrada no vuelve a abrirse por el enlace: misma respuesta genérica que un token inválido.
        if (
            request.IsFailure
            || request.Value.Status is FormRequestStatus.Completed or FormRequestStatus.Cancelled
            || request.Value.Participant.Kind != FormParticipantKind.External
            || request.Value.Participant.ParticipantId is not { } participantId
        )
            return null;
        var participant = await participants.GetAsync(grant.TenantId, participantId, ct);
        return participant is null ? null : new Context(grant, request.Value, participant);
    }

    internal static ClientFormParticipant MessageParticipant(Guid participantId) =>
        new() { Kind = nameof(FormParticipantKind.External), ParticipantId = participantId };
}
