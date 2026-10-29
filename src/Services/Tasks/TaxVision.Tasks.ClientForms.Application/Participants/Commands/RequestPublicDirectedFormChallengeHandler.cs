using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public static class RequestPublicDirectedFormChallengeHandler
{
    public static async Task<Result> Handle(
        RequestPublicDirectedFormChallengeCommand command,
        IFormAccessGrantRepository grants,
        IFormAccessChallengeRepository challenges,
        IFormRequestRepository requests,
        IFormParticipantRepository participants,
        IConsentNoticeRepository notices,
        IFormExternalAccessSecurity security,
        IFormPublicTenantHostValidator hostValidator,
        IClientFormsPublicCapabilityEvaluator capabilities,
        IOptions<ClientFormsOptions> options,
        IProtectedPayloadCodec payloadCodec,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        IClientFormsMetrics metrics,
        CancellationToken ct
    )
    {
        var context = await PublicDirectedFormAccess.LoadAsync(
            command.AccessToken,
            command.RequestHost,
            command.NowUtc,
            grants,
            requests,
            participants,
            security,
            hostValidator,
            capabilities,
            ct
        );
        if (context is null || !await CanIssueAsync(context, command.NowUtc, challenges, security, options, ct))
            return Result.Success();
        var issued = await IssueAsync(context, command.NowUtc, notices, security, ct);
        if (issued is not { } challenge)
            return Result.Success();
        challenges.Add(challenge.Challenge);
        // Guardar antes de publicar: un reintento en línea no deja salir un código sin su hash persistido.
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(CodeDelivery(context, challenge.Challenge, challenge.Code, payloadCodec, correlation));
        metrics.RecordExternalOtp(ClientFormsMetricValues.OtpIssued);
        return Result.Success();
    }

    private static Task<bool> CanIssueAsync(
        PublicDirectedFormAccess.Context context,
        DateTime nowUtc,
        IFormAccessChallengeRepository challenges,
        IFormExternalAccessSecurity security,
        IOptions<ClientFormsOptions> options,
        CancellationToken ct
    ) =>
        PublicFormChallengeIssuance.CanIssueAsync(
            context.Grant.TenantId,
            context.Grant.Id,
            null,
            context.Participant.Email,
            nowUtc,
            challenges,
            security,
            options,
            ct
        );

    // Genera el código y su desafío ligado al aviso de entrada fijado en el enlace; null si no aplica.
    private static async Task<(FormAccessChallenge Challenge, string Code)?> IssueAsync(
        PublicDirectedFormAccess.Context context,
        DateTime nowUtc,
        IConsentNoticeRepository notices,
        IFormExternalAccessSecurity security,
        CancellationToken ct
    )
    {
        var grant = context.Grant;
        var entryNotice = await EntryNoticeSupport.ResolvePinnedAsync(
            grant.TenantId,
            grant.EntryNoticeId,
            grant.EntryNoticeVersionNumber,
            notices,
            ct
        );
        if (entryNotice is null)
            return null;
        var email = context.Participant.Email;
        var code = security.GenerateOtp();
        var protectedEmail = security.ProtectEmail(grant.TenantId, email);
        var challenge = FormAccessChallenge.Create(
            grant.TenantId,
            grant.Id,
            null,
            protectedEmail.Cipher,
            protectedEmail.KeyVersion,
            protectedEmail.LookupHash,
            security.HashOtp(grant.TenantId, grant.Id, email, code),
            entryNotice.Version,
            nowUtc.Add(PublicFormChallengeIssuance.ChallengeLifetime),
            nowUtc
        );
        return challenge.IsFailure ? null : (challenge.Value, code);
    }

    private static FormAccessCodeDeliveryRequestedIntegrationEvent CodeDelivery(
        PublicDirectedFormAccess.Context context,
        FormAccessChallenge challenge,
        string code,
        IProtectedPayloadCodec payloadCodec,
        ICorrelationContext correlation
    )
    {
        var participant = PublicDirectedFormAccess.MessageParticipant(context.Participant.Id);
        var commandId = Guid.NewGuid();
        var payload = payloadCodec.Protect(
            ClientFormProtectedPayloadSchemas.AccessCode,
            new ProtectedPayloadBinding(
                context.Grant.TenantId,
                context.Request.Id,
                participant,
                ChallengeId: challenge.Id
            ),
            new FormAccessCodeDeliveryPayload(
                context.Participant.Email,
                code,
                context.Request.Title,
                context.Participant.Language
            )
        );
        return new FormAccessCodeDeliveryRequestedIntegrationEvent
        {
            EventId = commandId,
            CommandId = commandId,
            TenantId = context.Grant.TenantId,
            CorrelationId = correlation.CorrelationId,
            FormRequestId = context.Request.Id,
            ChallengeId = challenge.Id,
            Participant = participant,
            ExpiresAtUtc = challenge.ExpiresAtUtc,
            Payload = payload,
        };
    }
}
