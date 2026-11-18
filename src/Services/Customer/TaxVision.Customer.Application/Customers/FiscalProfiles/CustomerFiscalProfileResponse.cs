using TaxVision.Customer.Domain.FiscalProfiles;

namespace TaxVision.Customer.Application.Customers.FiscalProfiles;

public sealed record CustomerFiscalProfileResponse(
    Guid CustomerId,
    FiscalSubjectKind SubjectKind,
    // Null = SSN/EIN pendiente.
    string? TaxIdentifierLast4,
    FilingStatus? FilingStatus,
    decimal? PriorYearAgi,
    bool IsReturningCustomer,
    bool HasRefundBankInfo,
    DateTime UpdatedAtUtc,
    Guid UpdatedByUserId
);
