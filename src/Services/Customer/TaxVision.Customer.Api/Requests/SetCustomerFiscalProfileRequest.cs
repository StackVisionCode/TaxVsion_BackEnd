using TaxVision.Customer.Domain.FiscalProfiles;

namespace TaxVision.Customer.Api.Requests;

public sealed record SetCustomerFiscalProfileRequest(
    FiscalSubjectKind SubjectKind,
    // Opcional: al EDITAR un perfil existente, si viene vacío se conservan el identificador cifrado y
    // su last4, y solo se actualizan filing/AGI/returning (y banco si se envía). Obligatorio al crear.
    string? TaxIdentifier,
    FilingStatus? FilingStatus,
    decimal? PriorYearAgi,
    bool IsReturningCustomer,
    string? RefundBankAccount,
    string? RefundBankRouting
);
