using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Requests.Public;

public sealed record PublicSignerFieldView(
    Guid Id,
    SignatureFieldKind Kind,
    int Page,
    double X,
    double Y,
    double Width,
    double Height,
    string? Label,
    bool IsRequired
);

public sealed record PublicSignerView(
    Guid SignatureRequestId,
    Guid SignerId,
    string Title,
    string? Description,
    string Category,
    SignatureRequestStatus RequestStatus,
    SignerStatus SignerStatus,
    Guid OriginalFileId,
    bool RequiresConsent,
    bool HasAcceptedConsent,
    bool RequiresSequentialSigning,
    bool IsSignerNextInSequence,
    int Order,
    DateTime ExpiresAtUtc,
    string SignerFullName,
    string SignerEmail,
    bool RequiresPractitionerPin,
    bool IsPinVerified,
    DateTime? PinLockedUntilUtc,
    SignerVerificationMethod? RequiredVerificationMethod,
    bool IsVerificationCompleted,
    IReadOnlyList<PublicSignerFieldView> Fields,
    // Subdominio resuelto en el backend desde TenantBrandingRef. Vacío si no está proyectado:
    // la UI cae a su propia URL base y evita redirigir a un host inválido.
    string TenantSubDomain
);
