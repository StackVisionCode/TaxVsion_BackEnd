using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Requests.Public;

public sealed record PublicSignerView(
    Guid SignatureRequestId,
    Guid SignerId,
    string Title,
    string? Description,
    string Category,
    SignatureRequestStatus RequestStatus,
    SignerStatus SignerStatus,
    bool RequiresConsent,
    bool HasAcceptedConsent,
    bool RequiresSequentialSigning,
    bool IsSignerNextInSequence,
    int Order,
    DateTime? ExpiresAtUtc,
    string SignerFullName,
    string SignerEmail,
    bool RequiresPractitionerPin,
    bool IsPinVerified,
    DateTime? PinLockedUntilUtc,
    SignerVerificationMethod? RequiredVerificationMethod,
    bool IsVerificationCompleted,
    IReadOnlyList<PublicSignerDocumentView> Documents,
    IReadOnlyList<PublicSignerFieldView> Fields,
    string TenantSubDomain,
    bool PartialCopyWillBeSent,
    // Cómo se estampan sus campos (formato de fecha y rango de la letra automática).
    PublicStampStyleView StampStyle
);

public sealed record PublicStampStyleView(
    string DatePattern,
    int AutoFontMinPt,
    int AutoFontMaxPt,
    double AutoHeightRatio,
    // Piso al achicar un texto que no cabe.
    int MinFontSizePt
);
