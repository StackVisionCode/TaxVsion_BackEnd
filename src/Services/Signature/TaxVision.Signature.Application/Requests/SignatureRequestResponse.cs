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
    IReadOnlyList<SignatureFieldResponse> Fields,
    // F7 — estado de la copia inmediata que recibe este firmante tras firmar.
    DateTime? PartialCopyRequestedAtUtc,
    DateTime? PartialCopySentAtUtc,
    Guid? PartialCopyFileId,
    string? PartialCopyFailureReason
);

public sealed record SignatureFieldResponse(
    Guid Id,
    Guid SignerId,
    Guid DocumentId,
    SignatureFieldKind Kind,
    int Page,
    double X,
    double Y,
    double Width,
    double Height,
    string? Label,
    bool IsRequired
);

/// <summary>Campo del preparador (canal paralelo). Sin SignerId: no pertenece a un firmante.</summary>
public sealed record PreparerFieldResponse(
    Guid Id,
    Guid DocumentId,
    SignatureFieldKind Kind,
    int Page,
    double X,
    double Y,
    double Width,
    double Height,
    string? Label
);

public sealed record SignatureRequestDocumentResponse(
    Guid Id,
    int Order,
    string Title,
    Guid OriginalFileId,
    string? HashPre,
    Guid? SealedFileId,
    Guid? CertificateFileId,
    string? HashPost,
    DateTime? SealedAtUtc,
    string? Note
);

public sealed record SignatureRequestResponse(
    Guid Id,
    Guid TenantId,
    Guid CreatedByUserId,
    string Title,
    string? Description,
    string Category,
    SignatureRequestStatus Status,
    IReadOnlyList<SignatureRequestDocumentResponse> Documents,
    Guid? CertificateFileId,
    bool RequiresSequentialSigning,
    bool RequiresConsent,
    bool GenerateCertificate,
    CertificateGenerationMode CertificateGenerationMode,
    bool SendSealedDocumentToSigners,
    bool SendCertificateToSigners,
    bool AutoRemindersEnabled,
    int ReminderIntervalHours,
    bool RequiresPractitionerPin,
    DateTime? PractitionerPinSetAtUtc,
    // F7 — null si la expiración está desactivada.
    int? TokenExpirationHours,
    DateTime? ExpiresAtUtc,
    bool ExpirationEnabled,
    bool SendPartialCopyOnEachSignature,
    string PartialCopyAudienceKind,
    IReadOnlyList<Guid> PartialCopyAudienceSignerIds,
    int RevocationEpoch,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? SentAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CanceledAtUtc,
    DateTime? ExpiredAtUtc,
    // F3 — hora UTC programada para el envío (null si no está Scheduled).
    DateTime? ScheduledSendAtUtc,
    // Estado del preparador (canal paralelo Form 8879) — para rehidratar el editor y mostrar su firma.
    bool IsPreparerSigned,
    DateTime? PreparerSignedAtUtc,
    Guid? PreparerSignatureFileId,
    IReadOnlyList<PreparerFieldResponse> PreparerFields,
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
            request.Documents.OrderBy(document => document.Order).Select(MapDocument).ToList(),
            request.CertificateFileId,
            request.RequiresSequentialSigning,
            request.RequiresConsent,
            request.GenerateCertificate,
            request.CertificateGenerationMode,
            request.SendSealedDocumentToSigners,
            request.SendCertificateToSigners,
            request.AutoRemindersEnabled,
            request.ReminderIntervalHours,
            request.RequiresPractitionerPin,
            request.PractitionerPinSetAtUtc,
            request.TokenExpirationHours,
            request.ExpiresAtUtc,
            request.ExpirationEnabled,
            request.SendPartialCopyOnEachSignature,
            request.PartialCopyAudience.Kind.ToString(),
            [.. request.PartialCopyAudience.SpecificSignerIds],
            request.RevocationEpoch,
            request.CreatedAtUtc,
            request.UpdatedAtUtc,
            request.SentAtUtc,
            request.CompletedAtUtc,
            request.CanceledAtUtc,
            request.ExpiredAtUtc,
            request.ScheduledSendAtUtc,
            request.IsPreparerSigned,
            request.PreparerSignedAtUtc,
            request.PreparerSignatureFileId,
            request.PreparerFields.Select(MapPreparerField).ToList(),
            request.Signers.Select(MapSigner).ToList()
        );

    private static PreparerFieldResponse MapPreparerField(PreparerField field) =>
        new(
            field.Id,
            field.DocumentId,
            field.Kind,
            field.Position.Page,
            field.Position.X,
            field.Position.Y,
            field.Position.Width,
            field.Position.Height,
            field.Label
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
            signer.Fields.Select(f => MapField(signer.Id, f)).ToList(),
            signer.PartialCopyRequestedAtUtc,
            signer.PartialCopySentAtUtc,
            signer.PartialCopyFileId,
            signer.PartialCopyFailureReason
        );

    private static SignatureFieldResponse MapField(Guid signerId, SignatureField field) =>
        new(
            field.Id,
            signerId,
            field.DocumentId,
            field.Kind,
            field.Position.Page,
            field.Position.X,
            field.Position.Y,
            field.Position.Width,
            field.Position.Height,
            field.Label,
            field.IsRequired
        );

    private static SignatureRequestDocumentResponse MapDocument(RequestDocument document) =>
        new(
            document.Id,
            document.Order,
            document.Title,
            document.OriginalFileId,
            document.DocumentHashPre?.Value,
            document.SealedFileId,
            document.CertificateFileId,
            document.DocumentHashPost?.Value,
            document.SealedAtUtc,
            document.Note
        );
}
