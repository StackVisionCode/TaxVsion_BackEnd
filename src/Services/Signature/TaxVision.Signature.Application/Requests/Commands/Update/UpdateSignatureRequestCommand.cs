using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Requests.Commands.Update;

/// <summary>Edita la metadata de un borrador (Draft/Ready): título, categoría, expiración y entrega/reminders.</summary>
public sealed record UpdateSignatureRequestCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    string Title,
    string? Description,
    SignatureCategory Category,
    int TokenExpirationHours,
    // null = no cambiar (el detalle no expone estos flags; edición parcial).
    bool? SendSignedDocumentToSigners = null,
    bool? SendCertificateToSigners = null,
    bool? AutoRemindersEnabled = null,
    int? ReminderIntervalHours = null
);
