using TaxVision.Customer.Domain.Relations;

namespace TaxVision.Customer.Api.Requests;

public sealed record SetRelationFiscalProfileRequest(
    TaxRelationshipRole Role,
    string TaxIdentifier,
    int TaxYear,
    bool QualifiesAsDependent,
    bool LivedWithTaxpayer
);
