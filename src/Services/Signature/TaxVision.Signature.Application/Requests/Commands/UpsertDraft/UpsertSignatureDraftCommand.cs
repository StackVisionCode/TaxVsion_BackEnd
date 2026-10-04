using TaxVision.Signature.Domain.Requests;

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
    bool? SendSignedDocumentToSigners,
    bool? SendCertificateToSigners,
    bool? AutoRemindersEnabled,
    int? ReminderIntervalHours,
    IReadOnlyList<DraftSignerSpec> Signers,
    IReadOnlyList<DraftFieldSpec> Fields
);

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
    SignatureFieldKind Kind,
    int Page,
    double X,
    double Y,
    double Width,
    double Height,
    string? Label,
    bool IsRequired
);
