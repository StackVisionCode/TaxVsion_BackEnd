using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Requests.Commands.Create;

/// <summary>
/// Crea una solicitud de firma en estado <c>Draft</c>. No transiciona a <c>Ready</c>:
/// eso lo hace el consumer de <c>FileAvailable</c> cuando el archivo pasa el scan.
/// </summary>
public sealed record CreateSignatureRequestCommand(
    Guid TenantId,
    Guid CreatedByUserId,
    string Title,
    string? Description,
    SignatureCategory Category,
    Guid OriginalFileId,
    int TokenExpirationHours,
    bool RequiresSequentialSigning,
    bool RequiresConsent,
    bool GenerateCertificate
);
