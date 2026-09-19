using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Api.Requests;

public sealed record CreateSignatureRequestBody(
    string Title,
    string? Description,
    SignatureCategory Category,
    Guid OriginalFileId,
    int TokenExpirationHours,
    bool RequiresSequentialSigning,
    bool RequiresConsent,
    bool GenerateCertificate,
    // Default true = comportamiento histórico (se emailaba el documento firmado siempre). El gate de
    // permiso vive en el controller: sin signature.document.send estos quedan forzados a false.
    bool SendSignedDocumentToSigners = true,
    bool SendCertificateToSigners = false,
    // Recordatorios automáticos a firmantes: null = usar el default del tenant; con valor = override.
    bool? AutoRemindersEnabled = null,
    int? ReminderIntervalHours = null
);

public sealed record AddSignerBody(
    string Email,
    string FullName,
    string? PhoneNumber = null,
    string? Language = null,
    SignerVerificationMethod? VerificationMethod = null
);

public sealed record ReorderSignersBody(IReadOnlyList<Guid> OrderedSignerIds);

public sealed record PlaceFieldBody(
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

public sealed record CancelSignatureRequestBody(string? Reason);

public sealed record ExtendExpirationBody(int AdditionalHours);

public sealed record SetPractitionerPinBody(string Pin);

public sealed record SetPreparerBody(string PtinOrEfin, string DisplayName, string? TitleLabel);

public sealed record PlaceLegalHoldBody(string Reason);
