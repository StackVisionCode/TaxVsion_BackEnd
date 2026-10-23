using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Consents;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public sealed record GetPublicDirectedFormAccessQuery(string AccessToken, string RequestHost, DateTime NowUtc);

public sealed record RequestPublicDirectedFormChallengeCommand(string AccessToken, string RequestHost, DateTime NowUtc);

public sealed record VerifyPublicDirectedFormChallengeCommand(
    string AccessToken,
    string RequestHost,
    string Code,
    string ClientIp,
    string UserAgent,
    DateTime NowUtc
);

public sealed record ResolvePublicFormSessionCommand(string SessionToken, string RequestHost, DateTime NowUtc);

public sealed record RevokePublicFormSessionCommand(string SessionToken, string RequestHost, DateTime NowUtc);

public sealed record PublicDirectedFormAccessResponse(
    string Title,
    string TenantBrandingKey,
    string MaskedEmail,
    PublicEntryNoticeResponse EntryNotice
);

public sealed record PublicFormSessionResponse(string SessionToken, DateTime ExpiresAtUtc);

public static class PublicDirectedFormAccessErrors
{
    public static readonly Error NotFound = new("PublicForm.NotFound", "The form access link is unavailable.");
    public static readonly Error VerificationFailed = new(
        "PublicForm.VerificationFailed",
        "The verification code is invalid."
    );
}
