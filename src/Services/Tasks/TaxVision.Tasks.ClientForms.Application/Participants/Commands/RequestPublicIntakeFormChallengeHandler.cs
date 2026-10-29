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
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public static class RequestPublicIntakeFormChallengeHandler
{
    public static async Task<Result> Handle(
        RequestPublicIntakeFormChallengeCommand command,
        IFormIntakeLinkRepository links,
        IFormAccessChallengeRepository challenges,
        IFormTemplateVersionRepository versions,
        IConsentNoticeRepository notices,
        IFormExternalAccessSecurity security,
        IFormPublicTenantHostValidator hosts,
        IClientFormsPublicCapabilityEvaluator capabilities,
        IFormIntakeBotProtection botProtection,
        IProtectedPayloadCodec payloadCodec,
        IOptions<ClientFormsOptions> options,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        IClientFormsMetrics metrics,
        CancellationToken ct
    )
    {
        var email = ParticipantEmail.Create(command.Email);
        if (email.IsFailure || !await botProtection.VerifyAsync(command.CaptchaToken, command.ClientIp, ct))
            return Result.Success();
        var link = await PublicIntakeFormAccess.LoadAsync(
            command.IntakeToken,
            command.RequestHost,
            command.NowUtc,
            links,
            security,
            hosts,
            capabilities,
            ct
        );
        if (
            link is null
            || !await CanIssueAsync(link, email.Value.Value, command.NowUtc, challenges, security, options, ct)
        )
            return Result.Success();
        var issued = await IssueAsync(link, email.Value.Value, command.NowUtc, versions, notices, security, ct);
        if (issued is not { } challenge)
            return Result.Success();
        challenges.Add(challenge.Challenge);
        // Guardar antes de publicar: un reintento en línea no deja salir un código sin su hash persistido.
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            CodeDelivery(link, email.Value.Value, command.Language, challenge, payloadCodec, correlation)
        );
        metrics.RecordExternalOtp(ClientFormsMetricValues.OtpIssued);
        return Result.Success();
    }

    private static Task<bool> CanIssueAsync(
        FormIntakeLink link,
        string email,
        DateTime nowUtc,
        IFormAccessChallengeRepository challenges,
        IFormExternalAccessSecurity security,
        IOptions<ClientFormsOptions> options,
        CancellationToken ct
    ) =>
        PublicFormChallengeIssuance.CanIssueAsync(
            link.TenantId,
            null,
            link.Id,
            email,
            nowUtc,
            challenges,
            security,
            options,
            ct
        );

    // Genera el código ligado a la versión y al aviso de entrada fijados en el enlace; null si no aplica.
    private static async Task<(FormAccessChallenge Challenge, string Code, string Title)?> IssueAsync(
        FormIntakeLink link,
        string email,
        DateTime nowUtc,
        IFormTemplateVersionRepository versions,
        IConsentNoticeRepository notices,
        IFormExternalAccessSecurity security,
        CancellationToken ct
    )
    {
        var version = await versions.GetByIdAsync(link.TenantId, link.FormTemplateVersionId, ct);
        var entryNotice = version.IsFailure
            ? null
            : await EntryNoticeSupport.ResolvePinnedAsync(
                link.TenantId,
                link.EntryNoticeId,
                link.EntryNoticeVersionNumber,
                notices,
                ct
            );
        if (entryNotice is null)
            return null;
        var code = security.GenerateOtp();
        var protectedEmail = security.ProtectEmail(link.TenantId, email);
        var challenge = FormAccessChallenge.Create(
            link.TenantId,
            null,
            link.Id,
            protectedEmail.Cipher,
            protectedEmail.KeyVersion,
            protectedEmail.LookupHash,
            security.HashOtp(link.TenantId, link.Id, email, code),
            entryNotice.Version,
            nowUtc.Add(PublicFormChallengeIssuance.ChallengeLifetime),
            nowUtc
        );
        return challenge.IsFailure ? null : (challenge.Value, code, version.Value.Title);
    }

    private static FormAccessCodeDeliveryRequestedIntegrationEvent CodeDelivery(
        FormIntakeLink link,
        string email,
        string? language,
        (FormAccessChallenge Challenge, string Code, string Title) issued,
        IProtectedPayloadCodec payloadCodec,
        ICorrelationContext correlation
    )
    {
        var challenge = issued.Challenge;
        var commandId = Guid.NewGuid();
        var payload = payloadCodec.Protect(
            ClientFormProtectedPayloadSchemas.AccessCode,
            new ProtectedPayloadBinding(link.TenantId, null, null, link.Id, challenge.Id),
            // El idioma llega tal cual del navegador ("es"): se normaliza a En/Es antes del payload.
            new FormAccessCodeDeliveryPayload(
                email,
                issued.Code,
                issued.Title,
                FormParticipant.NormalizeLanguage(language)
            )
        );
        return new FormAccessCodeDeliveryRequestedIntegrationEvent
        {
            EventId = commandId,
            CommandId = commandId,
            TenantId = link.TenantId,
            CorrelationId = correlation.CorrelationId,
            FormRequestId = null,
            IntakeLinkId = link.Id,
            ChallengeId = challenge.Id,
            Participant = null,
            ExpiresAtUtc = challenge.ExpiresAtUtc,
            Payload = payload,
        };
    }
}
