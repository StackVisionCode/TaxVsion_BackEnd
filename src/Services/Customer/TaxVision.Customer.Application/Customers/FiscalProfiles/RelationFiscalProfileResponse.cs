using TaxVision.Customer.Domain.Relations;

namespace TaxVision.Customer.Application.Customers.FiscalProfiles;

public sealed record RelationFiscalProfileResponse(
    Guid CustomerRelationId,
    TaxRelationshipRole Role,
    string TaxIdentifierLast4,
    int TaxYear,
    bool QualifiesAsDependent,
    bool LivedWithTaxpayer,
    DateTime UpdatedAtUtc,
    Guid UpdatedByUserId
);
