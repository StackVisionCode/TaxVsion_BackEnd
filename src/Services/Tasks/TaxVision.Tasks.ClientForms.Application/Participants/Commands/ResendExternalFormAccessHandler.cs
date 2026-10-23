using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public sealed record ResendExternalFormAccessCommand(
    Guid TenantId,
    Guid ByUserId,
    bool CanViewAll,
    Guid FormRequestId,
    DateTime NowUtc
);

public static class ResendExternalFormAccessHandler
{
    private static readonly TimeSpan GrantLifetime = TimeSpan.FromDays(14);

    public static async Task<Result> Handle(
        ResendExternalFormAccessCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        IFormParticipantRepository participants,
        IFormAccessGrantRepository grants,
        IClientFormsPublicCapabilityEvaluator capabilities,
        IFormExternalAccessSecurity security,
        IProtectedPayloadCodec payloadCodec,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (!await capabilities.IsExternalParticipantAvailableAsync(command.TenantId, ct))
            return Result.Failure(FormRequestErrors.NotFound);
        var request = await LoadVisibleExternalRequestAsync(command, requests, visibility, ct);
        if (request is null || request.Participant.ParticipantId is not { } participantId)
            return Result.Failure(FormRequestErrors.NotFound);
        var participant = await participants.GetAsync(command.TenantId, participantId, ct);
        var grant = await grants.GetByRequestAsync(command.TenantId, command.FormRequestId, ct);
        if (participant is null || grant is null)
            return Result.Failure(FormRequestErrors.NotFound);

        var token = security.GenerateToken();
        var expiresAtUtc = command.NowUtc.Add(GrantLifetime);
        var rotated = grant.Rotate(token.TokenHash, token.TokenLast4, expiresAtUtc, command.NowUtc);
        if (rotated.IsFailure)
            return rotated;

        await PublishDeliveryAsync(command, request, participant, token, expiresAtUtc, payloadCodec, bus, correlation);
        audit.Record(
            command.TenantId,
            request.FormTemplateId,
            request.Id,
            "request.external_access_resent",
            command.ByUserId
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    // Lo ajeno, lo no visible y lo que no es de un participante externo responden igual: no existe.
    private static async Task<FormRequest?> LoadVisibleExternalRequestAsync(
        ResendExternalFormAccessCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        CancellationToken ct
    )
    {
        var found = await requests.GetForCommandAsync(command.TenantId, command.FormRequestId, ct);
        if (
            found.IsFailure
            || !await visibility.CanViewAsync(command.TenantId, command.ByUserId, command.CanViewAll, found.Value, ct)
            || found.Value.Participant.Kind != FormParticipantKind.External
        )
            return null;
        return found.Value;
    }

    private static async Task PublishDeliveryAsync(
        ResendExternalFormAccessCommand command,
        FormRequest request,
        FormParticipant participant,
        FormOpaqueToken token,
        DateTime expiresAtUtc,
        IProtectedPayloadCodec payloadCodec,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
        var messageParticipant = PublicDirectedFormAccess.MessageParticipant(participant.Id);
        var commandId = Guid.NewGuid();
        var payload = payloadCodec.Protect(
            ClientFormProtectedPayloadSchemas.AccessLink,
            new ProtectedPayloadBinding(command.TenantId, request.Id, messageParticipant),
            new FormAccessLinkDeliveryPayload(participant.Email, participant.FullName, token.RawToken, request.Title)
        );
        await bus.PublishAsync(
            new FormAccessLinkDeliveryRequestedIntegrationEvent
            {
                EventId = commandId,
                CommandId = commandId,
                TenantId = command.TenantId,
                CorrelationId = correlation.CorrelationId,
                FormRequestId = request.Id,
                Participant = messageParticipant,
                ExpiresAtUtc = expiresAtUtc,
                Payload = payload,
            }
        );
    }
}
