namespace TaxVision.Tasks.Api.Requests.ClientForms;

// Language: el idioma que el externo eligió en la página pública (P-21.5); null conserva el guardado.
public sealed record VerifyPublicFormAccessRequest(string Code, string? Language = null);

public sealed record RequestPublicIntakeChallengeRequest(string Email, string CaptchaToken, string? Language = null);

public sealed record VerifyPublicIntakeChallengeRequest(string Email, string Code, string? Language = null);

public sealed record InitiatePublicFormAttachmentRequest(
    string ComponentId,
    string? RowId,
    string OriginalName,
    string ContentType,
    long SizeBytes
);
