using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Application.Requests.Commands.Create;

/// <summary>
/// Crea una solicitud de firma en estado <c>Draft</c>. No transiciona a <c>Ready</c>:
/// eso lo hace el consumer de <c>FileAvailable</c> cuando el archivo pasa el scan.
/// Null en los flags → se resuelve con el default del tenant.
/// </summary>
public sealed record CreateSignatureRequestCommand(
    Guid TenantId,
    Guid CreatedByUserId,
    string Title,
    string? Description,
    string Category,
    Guid OriginalFileId,
    int TokenExpirationHours,
    bool RequiresSequentialSigning,
    bool RequiresConsent,
    bool GenerateCertificate,
    bool? SendSealedDocumentToSigners = null,
    bool SendCertificateToSigners = false,
    // null = usar el default del tenant (TenantSignatureSettings). Con valor = override del preparador.
    bool? AutoRemindersEnabled = null,
    int? ReminderIntervalHours = null,
    // F7 — copia parcial + expiración opcional. Null → default del tenant.
    bool? SendPartialCopyOnEachSignature = null,
    PartialCopyAudience? PartialCopyAudience = null,
    bool? ExpirationEnabled = null
);
