namespace TaxVision.Tasks.Api.Requests.ClientForms;

public sealed record VerifyPublicFormAccessRequest(string Code);

public sealed record RequestPublicIntakeChallengeRequest(string Email, string CaptchaToken);

public sealed record VerifyPublicIntakeChallengeRequest(string Email, string Code);

public sealed record InitiatePublicFormAttachmentRequest(
    string ComponentId,
    string? RowId,
    string OriginalName,
    string ContentType,
    long SizeBytes
);
