using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public static class VerifyPublicIntakeFormChallengeHandler
{
    public static async Task<Result<PublicIntakeFormSessionResponse>> Handle(
        VerifyPublicIntakeFormChallengeCommand command,
        IFormIntakeLinkRepository links,
        IFormAccessChallengeRepository challenges,
        IFormAccessSessionRepository sessions,
        IFormParticipantRepository participants,
        IFormRequestRepository requests,
        IFormTemplateVersionRepository versions,
        IConsentNoticeRepository notices,
        IFormConsentRecordRepository consentRecords,
        IFormExternalAccessSecurity security,
        IFormPublicTenantHostValidator hosts,
        IClientFormsPublicCapabilityEvaluator capabilities,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IClientFormsMetrics metrics,
        CancellationToken ct
    )
    {
        var link = await LoadLinkAsync(command, links, security, hosts, capabilities, ct);
        var challenge = await VerifyCodeAsync(command, link, challenges, security, metrics, unitOfWork, ct);
        if (link is null || challenge is null)
            return Failed();
        var participant = await ResolveParticipantAsync(
            link,
            challenge,
            command.NowUtc,
            command.Language,
            participants,
            security,
            unitOfWork,
            ct
        );
        var request = await GetOrCreateRequestAsync(link, participant, command.NowUtc, requests, versions, ct);
        if (participant is null || request is null)
            return Failed();
        return await OpenSessionAsync(
            command,
            request,
            participant,
            challenge,
            sessions,
            security,
            notices,
            consentRecords,
            audit,
            unitOfWork,
            ct
        );
    }

    private static async Task<FormIntakeLink?> LoadLinkAsync(
        VerifyPublicIntakeFormChallengeCommand command,
        IFormIntakeLinkRepository links,
        IFormExternalAccessSecurity security,
        IFormPublicTenantHostValidator hosts,
        IClientFormsPublicCapabilityEvaluator capabilities,
        CancellationToken ct
    )
    {
        if (ParticipantEmail.Create(command.Email).IsFailure)
            return null;
        return await PublicIntakeFormAccess.LoadAsync(
            command.IntakeToken,
            command.RequestHost,
            command.NowUtc,
            links,
            security,
            hosts,
            capabilities,
            ct
        );
    }

    // Devuelve el código ya consumido, o null si no verificó.
    private static async Task<FormAccessChallenge?> VerifyCodeAsync(
        VerifyPublicIntakeFormChallengeCommand command,
        FormIntakeLink? link,
        IFormAccessChallengeRepository challenges,
        IFormExternalAccessSecurity security,
        IClientFormsMetrics metrics,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (link is null)
            return null;
        var email = ParticipantEmail.Create(command.Email).Value.Value;
        var challenge = await PublicFormChallengeVerification.LoadVerifiableAsync(
            link.TenantId,
            null,
            link.Id,
            email,
            command.NowUtc,
            challenges,
            security,
            ct
        );
        if (challenge is null)
            return null;
        var matches = security.VerifyOtp(link.TenantId, link.Id, email, command.Code, challenge.CodeHash);
        var verified = await PublicFormChallengeVerification.VerifyAsync(
            challenge,
            matches,
            command.NowUtc,
            unitOfWork,
            metrics,
            ct
        );
        return verified ? challenge : null;
    }

    // Reutiliza el participante sin vincular del email o lo crea; queda verificado.
    private static async Task<FormParticipant?> ResolveParticipantAsync(
        FormIntakeLink link,
        FormAccessChallenge challenge,
        DateTime nowUtc,
        string? language,
        IFormParticipantRepository participants,
        IFormExternalAccessSecurity security,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var email = security.UnprotectEmail(challenge.EmailCipher, challenge.EmailKeyVersion);
        var participant =
            await participants.GetUnlinkedByEmailAsync(link.TenantId, email, ct)
            ?? await CreateParticipantAsync(link, email, nowUtc, language, participants, unitOfWork, ct);
        if (language is not null)
            participant?.SetLanguage(language);
        return participant is not null && participant.MarkVerified(nowUtc).IsSuccess ? participant : null;
    }

    private static async Task<FormParticipant?> CreateParticipantAsync(
        FormIntakeLink link,
        string email,
        DateTime nowUtc,
        string? language,
        IFormParticipantRepository participants,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var created = FormParticipant.Create(
            link.TenantId,
            email,
            null,
            null,
            FormParticipantSource.IntakeLink,
            nowUtc,
            FormParticipant.NormalizeLanguage(language)
        );
        if (created.IsFailure)
            return null;
        participants.Add(created.Value);
        if (await PublicFormChallengeVerification.TrySaveAsync(unitOfWork, ct))
            return created.Value;
        // Otra verificación del mismo email ganó el índice único: se usa la fila ganadora. Si el choque
        // fue del código, el guardado final vuelve a chocar y la verificación falla.
        participants.Untrack(created.Value);
        return await participants.GetUnlinkedByEmailAsync(link.TenantId, email, ct);
    }

    private static async Task<FormRequest?> GetOrCreateRequestAsync(
        FormIntakeLink link,
        FormParticipant? participant,
        DateTime nowUtc,
        IFormRequestRepository requests,
        IFormTemplateVersionRepository versions,
        CancellationToken ct
    )
    {
        if (participant is null)
            return null;
        var existing = await requests.GetOpenExternalRequestAsync(
            link.TenantId,
            participant.Id,
            link.FormTemplateId,
            ct
        );
        if (existing is not null)
            return existing;
        var version = await versions.GetByIdAsync(link.TenantId, link.FormTemplateVersionId, ct);
        if (version.IsFailure)
            return null;
        var created = CreateIntakeRequest(link, participant, version.Value, nowUtc);
        if (created.IsFailure)
            return null;
        requests.Add(created.Value);
        return created.Value;
    }

    private static Result<FormRequest> CreateIntakeRequest(
        FormIntakeLink link,
        FormParticipant participant,
        FormTemplateVersion version,
        DateTime nowUtc
    ) =>
        FormRequest.Create(
            link.TenantId,
            FormParticipantRef.External(participant.Id),
            version,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            FormRequestOrigin.IntakeLink,
            link.Id,
            link.ResponsibleUserId,
            link.ResponsibleUserId,
            nowUtc
        );

    private static async Task<Result<PublicIntakeFormSessionResponse>> OpenSessionAsync(
        VerifyPublicIntakeFormChallengeCommand command,
        FormRequest request,
        FormParticipant participant,
        FormAccessChallenge challenge,
        IFormAccessSessionRepository sessions,
        IFormExternalAccessSecurity security,
        IConsentNoticeRepository notices,
        IFormConsentRecordRepository consentRecords,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var opened = await PublicFormSessionOpener.OpenAsync(
            request.TenantId,
            request.Id,
            participant.Id,
            challenge,
            command.ClientIp,
            command.UserAgent,
            command.NowUtc,
            sessions,
            security,
            notices,
            consentRecords,
            audit,
            ct
        );
        if (opened is not { } session || !await PublicFormChallengeVerification.TrySaveAsync(unitOfWork, ct))
            return Failed();
        return Result.Success(
            new PublicIntakeFormSessionResponse(session.RawToken, session.Session.ExpiresAtUtc, request.Id)
        );
    }

    private static Result<PublicIntakeFormSessionResponse> Failed() =>
        Result.Failure<PublicIntakeFormSessionResponse>(PublicIntakeFormErrors.VerificationFailed);
}
