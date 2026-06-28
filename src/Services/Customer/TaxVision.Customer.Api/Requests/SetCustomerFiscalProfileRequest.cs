using TaxVision.Customer.Domain.FiscalProfiles;

namespace TaxVision.Customer.Api.Requests;

public sealed record SetCustomerFiscalProfileRequest(
    FiscalSubjectKind SubjectKind,
    string TaxIdentifier,
    FilingStatus? FilingStatus,
    decimal? PriorYearAgi,
    bool IsReturningCustomer,
    string? RefundBankAccount,
    string? RefundBankRouting
);
