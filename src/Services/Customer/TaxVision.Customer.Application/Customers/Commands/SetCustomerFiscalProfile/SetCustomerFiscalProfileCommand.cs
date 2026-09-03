using TaxVision.Customer.Domain.FiscalProfiles;

namespace TaxVision.Customer.Application.Customers.Commands.SetCustomerFiscalProfile;

public sealed record SetCustomerFiscalProfileCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid ModifiedByUserId,
    FiscalSubjectKind SubjectKind,
    // SSN/ITIN/EIN en plain — se normaliza y cifra en el handler. Opcional al EDITAR: vacío conserva
    // el identificador actual y solo actualiza filing/AGI/returning (+ banco si viene).
    string? TaxIdentifier,
    FilingStatus? FilingStatus,
    decimal? PriorYearAgi,
    bool IsReturningCustomer,
    string? RefundBankAccount, // opcional, plain text
    string? RefundBankRouting // opcional, plain text
);
