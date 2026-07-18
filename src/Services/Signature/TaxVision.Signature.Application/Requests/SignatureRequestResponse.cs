using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Requests;

public sealed record SignerResponse(
    Guid Id,
    string Email,
    string FullName,
    Guid? MappedCustomerId,
    int Order,
    SignerStatus Status,
    DateTime? SignedAtUtc,
    IReadOnlyList<SignatureFieldResponse> Fields
);

public sealed record SignatureFieldResponse(
    Guid Id,
    Guid SignerId,
    SignatureFieldKind Kind,
    int Page,
    double X,
    double Y,
    double Width,
    double Height,
    string? Label,
    bool IsRequired
);

public sealed record SignatureRequestResponse(
    Guid Id,
    Guid TenantId,
    Guid CreatedByUserId,
    string Title,
    string? Description,
    SignatureCategory Category,
    SignatureRequestStatus Status,
    Guid OriginalFileId,
    string? DocumentHashPre,
    Guid? SealedFileId,
    string? DocumentHashPost,
    Guid? CertificateFileId,
    bool RequiresSequentialSigning,
    bool RequiresConsent,
    bool GenerateCertificate,
    bool RequiresPractitionerPin,
    DateTime? PractitionerPinSetAtUtc,
    int TokenExpirationHours,
    DateTime ExpiresAtUtc,
    int RevocationEpoch,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? SentAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CanceledAtUtc,
    DateTime? ExpiredAtUtc,
    IReadOnlyList<SignerResponse> Signers
)
{
    public static SignatureRequestResponse From(SignatureRequest request) =>
        new(
            request.Id,
            request.TenantId,
            request.CreatedByUserId,
            request.Title,
            request.Description,
            request.Category,
            request.Status,
            request.OriginalFileId,
            request.DocumentHashPre?.Value,
            request.SealedFileId,
            request.DocumentHashPost?.Value,
            request.CertificateFileId,
            request.RequiresSequentialSigning,
            request.RequiresConsent,
            request.GenerateCertificate,
            request.RequiresPractitionerPin,
            request.PractitionerPinSetAtUtc,
            request.TokenExpirationHours,
            request.ExpiresAtUtc,
            request.RevocationEpoch,
            request.CreatedAtUtc,
            request.UpdatedAtUtc,
            request.SentAtUtc,
            request.CompletedAtUtc,
            request.CanceledAtUtc,
            request.ExpiredAtUtc,
            request.Signers.Select(MapSigner).ToList()
        );

    private static SignerResponse MapSigner(Signer signer) =>
        new(
            signer.Id,
            signer.Email.Value,
            signer.FullName.Value,
            signer.MappedCustomerId,
            signer.Order,
            signer.Status,
            signer.SignedAtUtc,
            signer.Fields.Select(f => MapField(signer.Id, f)).ToList()
        );

    private static SignatureFieldResponse MapField(Guid signerId, SignatureField field) =>
        new(
            field.Id,
            signerId,
            field.Kind,
            field.Position.Page,
            field.Position.X,
            field.Position.Y,
            field.Position.Width,
            field.Position.Height,
            field.Label,
            field.IsRequired
        );
}
