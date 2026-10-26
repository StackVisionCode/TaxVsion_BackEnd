using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Consents;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public sealed record GetPublicIntakeFormAccessQuery(string IntakeToken, string RequestHost, DateTime NowUtc);

public sealed record RequestPublicIntakeFormChallengeCommand(
    string IntakeToken,
    string RequestHost,
    string Email,
    string CaptchaToken,
    string ClientIp,
    DateTime NowUtc,
    string? Language = null
);

public sealed record VerifyPublicIntakeFormChallengeCommand(
    string IntakeToken,
    string RequestHost,
    string Email,
    string Code,
    string ClientIp,
    string UserAgent,
    DateTime NowUtc,
    // Null conserva el idioma ya guardado del participante.
    string? Language = null
);

public sealed record PublicIntakeFormAccessResponse(
    string Title,
    string TenantBrandingKey,
    PublicEntryNoticeResponse EntryNotice,
    PublicFormBotProtectionResponse BotProtection
);

public sealed record PublicFormBotProtectionResponse(string Provider, string SiteKey, string Action);

public sealed record PublicIntakeFormSessionResponse(string SessionToken, DateTime ExpiresAtUtc, Guid RequestId);

public static class PublicIntakeFormErrors
{
    public static readonly Error NotFound = new("PublicForm.NotFound", "The form access link is unavailable.");
    public static readonly Error VerificationFailed = new(
        "PublicForm.VerificationFailed",
        "The verification code is invalid."
    );
}
