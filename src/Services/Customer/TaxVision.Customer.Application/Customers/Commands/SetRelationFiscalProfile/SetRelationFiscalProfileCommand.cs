using TaxVision.Customer.Domain.Relations;

namespace TaxVision.Customer.Application.Customers.Commands.SetRelationFiscalProfile;

public sealed record SetRelationFiscalProfileCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid RelationId,
    Guid ModifiedByUserId,
    TaxRelationshipRole Role,
    string TaxIdentifier,
    int TaxYear,
    bool QualifiesAsDependent,
    bool LivedWithTaxpayer
);
