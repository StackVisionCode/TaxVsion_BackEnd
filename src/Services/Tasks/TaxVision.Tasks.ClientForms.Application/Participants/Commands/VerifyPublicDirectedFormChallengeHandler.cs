using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public static class VerifyPublicDirectedFormChallengeHandler
{
    public static async Task<Result<PublicFormSessionResponse>> Handle(
        VerifyPublicDirectedFormChallengeCommand command,
        IFormAccessGrantRepository grants,
        IFormAccessChallengeRepository challenges,
        IFormAccessSessionRepository sessions,
        IFormRequestRepository requests,
        IFormParticipantRepository participants,
        IConsentNoticeRepository notices,
        IFormConsentRecordRepository consentRecords,
        IFormExternalAccessSecurity security,
        IFormPublicTenantHostValidator hostValidator,
        IClientFormsPublicCapabilityEvaluator capabilities,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
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
        var challenge = await VerifyCodeAsync(command, context, challenges, security, metrics, unitOfWork, ct);
        if (context is null || challenge is null || !RecordVerified(context, command))
            return Failed();
        return await OpenSessionAsync(
            command,
            context,
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

    // Devuelve el código ya consumido, o null si no verificó.
    private static async Task<FormAccessChallenge?> VerifyCodeAsync(
        VerifyPublicDirectedFormChallengeCommand command,
        PublicDirectedFormAccess.Context? context,
        IFormAccessChallengeRepository challenges,
        IFormExternalAccessSecurity security,
        IClientFormsMetrics metrics,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (context is null)
            return null;
        var tenantId = context.Grant.TenantId;
        var email = context.Participant.Email;
        var challenge = await PublicFormChallengeVerification.LoadVerifiableAsync(
            tenantId,
            context.Grant.Id,
            null,
            email,
            command.NowUtc,
            challenges,
            security,
            ct
        );
        if (challenge is null)
            return null;
        var matches = security.VerifyOtp(tenantId, context.Grant.Id, email, command.Code, challenge.CodeHash);
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

    private static bool RecordVerified(
        PublicDirectedFormAccess.Context context,
        VerifyPublicDirectedFormChallengeCommand command
    )
    {
        if (command.Language is not null)
            context.Participant.SetLanguage(command.Language);
        return context.Participant.MarkVerified(command.NowUtc).IsSuccess
            && context.Grant.RecordUsed(command.NowUtc).IsSuccess;
    }

    private static async Task<Result<PublicFormSessionResponse>> OpenSessionAsync(
        VerifyPublicDirectedFormChallengeCommand command,
        PublicDirectedFormAccess.Context context,
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
            context.Grant.TenantId,
            context.Request.Id,
            context.Participant.Id,
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
        return Result.Success(new PublicFormSessionResponse(session.RawToken, session.Session.ExpiresAtUtc));
    }

    private static Result<PublicFormSessionResponse> Failed() =>
        Result.Failure<PublicFormSessionResponse>(PublicDirectedFormAccessErrors.VerificationFailed);
}
