using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Api.Requests;

public sealed record CreateSignatureRequestBody(
    string Title,
    string? Description,
    string Category,
    IReadOnlyList<CreateSignatureRequestDocumentBody> Documents,
    int TokenExpirationHours,
    bool RequiresSequentialSigning,
    bool RequiresConsent,
    bool GenerateCertificate,
    // null = default del tenant. El gate de permiso vive en el controller: sin signature.document.send
    // estos quedan forzados a false.
    bool? SendSealedDocumentToSigners = null,
    bool SendCertificateToSigners = false,
    // Recordatorios automáticos: null = usar el default del tenant; con valor = override.
    bool? AutoRemindersEnabled = null,
    int? ReminderIntervalHours = null,
    // F7 — copia parcial + expiración opcional. null = default del tenant.
    bool? SendPartialCopyOnEachSignature = null,
    PartialCopyAudienceBody? PartialCopyAudience = null,
    bool? ExpirationEnabled = null,
    CertificateGenerationMode CertificateGenerationMode = CertificateGenerationMode.SingleForRequest
);

// Edición de metadata de un borrador (Draft/Ready). GenerateCertificate y el documento no se editan
// aquí: son decisiones de creación. Los flags son OPCIONALES (null = no tocar).
public sealed record UpdateSignatureRequestBody(
    string Title,
    string? Description,
    string Category,
    int TokenExpirationHours,
    bool? SendSealedDocumentToSigners = null,
    bool? SendCertificateToSigners = null,
    bool? AutoRemindersEnabled = null,
    int? ReminderIntervalHours = null,
    // F7 — null = no tocar.
    bool? SendPartialCopyOnEachSignature = null,
    PartialCopyAudienceBody? PartialCopyAudience = null,
    bool? ExpirationEnabled = null,
    CertificateGenerationMode? CertificateGenerationMode = null
);

/// <summary>F7 — audiencia de la copia parcial. Kind=All ignora SignerIds; Kind=Specific exige lista no vacía.</summary>
public sealed record PartialCopyAudienceBody(string Kind, IReadOnlyList<Guid>? SignerIds = null);

public sealed record AddSignerBody(
    string Email,
    string FullName,
    string? PhoneNumber = null,
    string? Language = null,
    SignerVerificationMethod? VerificationMethod = null
);

public sealed record ReorderSignersBody(IReadOnlyList<Guid> OrderedSignerIds);

public sealed record AddRequestDocumentBody(
    Guid OriginalFileId,
    string Title,
    string? Note = null,
    int? PageCount = null
);

public sealed record ReorderDocumentsBody(IReadOnlyList<Guid> OrderedDocumentIds);

// F9 — Reemplazo de documentos. PageCount viene del preflight; si cambia respecto del PDF viejo, el
// backend invalida los campos de ese documento y lo reporta en la respuesta (`FieldsInvalidated`).
public sealed record ReplaceRequestDocumentFileBody(Guid NewFileId, int? NewPageCount = null);

public sealed record PlaceFieldBody(
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

public sealed record CancelSignatureRequestBody(string? Reason);

// F3 — programar envío. UTC siempre; la UI convierte desde la zona de la oficina.
public sealed record ScheduleSendBody(DateTime ScheduledSendAtUtc);

public sealed record ExtendExpirationBody(int AdditionalHours);

public sealed record SetPractitionerPinBody(string Pin);

public sealed record SetPreparerBody(string PtinOrEfin, string DisplayName, string? TitleLabel);

public sealed record PlacePreparerFieldBody(
    Guid DocumentId,
    SignatureFieldKind Kind,
    int Page,
    double X,
    double Y,
    double Width,
    double Height,
    string? Label
);

/// <summary>Firma reutilizable a estampar por el preparador. Null = usar la firma efectiva (personal u oficina).</summary>
public sealed record SetPreparerSignatureBody(Guid? SignatureFileId);

public sealed record PlaceLegalHoldBody(string Reason);

// Autosave: estado completo del editor en un solo POST. ExpectedUpdatedAtUtc = optimistic concurrency.
public sealed record UpsertDraftBody(
    DateTime? ExpectedUpdatedAtUtc,
    string Title,
    string? Description,
    string Category,
    int TokenExpirationHours,
    bool? SendSealedDocumentToSigners,
    bool? SendCertificateToSigners,
    bool? AutoRemindersEnabled,
    int? ReminderIntervalHours,
    IReadOnlyList<UpsertDraftDocumentBody> Documents,
    IReadOnlyList<UpsertDraftSignerBody> Signers,
    IReadOnlyList<UpsertDraftFieldBody> Fields,
    // F7 — null = no tocar.
    bool? SendPartialCopyOnEachSignature = null,
    PartialCopyAudienceBody? PartialCopyAudience = null,
    bool? ExpirationEnabled = null,
    CertificateGenerationMode? CertificateGenerationMode = null
);

public sealed record UpsertDraftDocumentBody(string LocalId, Guid? Id, Guid OriginalFileId, string Title, string? Note);

public sealed record UpsertDraftSignerBody(
    Guid? Id,
    string Email,
    string FullName,
    string? PhoneNumber,
    string? Language,
    SignerVerificationMethod? VerificationMethod
);

public sealed record UpsertDraftFieldBody(
    Guid? Id,
    int SignerIndex,
    string DocumentLocalId,
    SignatureFieldKind Kind,
    int Page,
    double X,
    double Y,
    double Width,
    double Height,
    string? Label,
    bool IsRequired
);
