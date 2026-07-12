using TaxVision.Customer.Domain.FiscalProfiles;

namespace TaxVision.Customer.Application.Customers.FiscalProfiles;

public sealed record RevealedTaxIdentifierResponse(
    Guid CustomerId,
    FiscalSubjectKind SubjectKind,
    string TaxIdentifier
);
