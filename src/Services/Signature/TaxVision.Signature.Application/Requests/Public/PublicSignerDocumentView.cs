namespace TaxVision.Signature.Application.Requests.Public;

public sealed record PublicSignerDocumentView(
    Guid DocumentId,
    string Title,
    int Order,
    bool HasFieldsToSign,
    DateTime? FirstViewedAtUtc,
    DateTime? SignedAtUtc
);
