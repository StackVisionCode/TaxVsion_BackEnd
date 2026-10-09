using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Application.Requests.Commands.Update;

/// <summary>Edita la metadata de un borrador (Draft/Ready): título, categoría, expiración y entrega/reminders.</summary>
public sealed record UpdateSignatureRequestCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    string Title,
    string? Description,
    string Category,
    int TokenExpirationHours,
    // null = no cambiar (el detalle no expone estos flags; edición parcial).
    bool? SendSealedDocumentToSigners = null,
    bool? SendCertificateToSigners = null,
    bool? AutoRemindersEnabled = null,
    int? ReminderIntervalHours = null,
    // F7 — null = no tocar; audiencia obligatoria cuando SendPartialCopy pasa a true.
    bool? SendPartialCopyOnEachSignature = null,
    PartialCopyAudience? PartialCopyAudience = null,
    bool? ExpirationEnabled = null,
    CertificateGenerationMode? CertificateGenerationMode = null
);
