using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Application.Requests.Commands.UpsertDraft;

/// <summary>
/// Autosave: reconcilia metadata + signers + fields de un borrador existente en una sola
/// transacción. ExpectedUpdatedAtUtc es el token de concurrencia: null = cliente no verifica.
/// </summary>
public sealed record UpsertSignatureDraftCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    DateTime? ExpectedUpdatedAtUtc,
    string Title,
    string? Description,
    string Category,
    int TokenExpirationHours,
    bool? SendSealedDocumentToSigners,
    bool? SendCertificateToSigners,
    bool? AutoRemindersEnabled,
    int? ReminderIntervalHours,
    IReadOnlyList<DraftDocumentSpec> Documents,
    IReadOnlyList<DraftSignerSpec> Signers,
    IReadOnlyList<DraftFieldSpec> Fields,
    // F7 — null = no tocar. Si SendPartialCopy pasa a true, PartialCopyAudience es requerida.
    bool? SendPartialCopyOnEachSignature = null,
    PartialCopyAudience? PartialCopyAudience = null,
    bool? ExpirationEnabled = null,
    CertificateGenerationMode? CertificateGenerationMode = null
);

public sealed record DraftDocumentSpec(string LocalId, Guid? Id, Guid OriginalFileId, string Title, string? Note);

// Guardrail #2: identidad explícita. Id null = crear; Id presente = reconciliar; ausente del payload = borrar.
public sealed record DraftSignerSpec(
    Guid? Id,
    string Email,
    string FullName,
    string? PhoneNumber,
    string? Language,
    SignerVerificationMethod? VerificationMethod
);

// SignerIndex ata el campo a su firmante dentro del MISMO payload (sirve para firmantes recién creados).
public sealed record DraftFieldSpec(
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
