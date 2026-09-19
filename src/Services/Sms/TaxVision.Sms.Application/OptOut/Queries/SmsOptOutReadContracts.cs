using TaxVision.Sms.Domain.OptOut;

namespace TaxVision.Sms.Application.OptOut.Queries;

/// <summary>Filtro de consentimiento para el listado de bajas. <see cref="All"/> no filtra.</summary>
public enum SmsOptOutStatusFilter
{
    All = 0,
    OptedOut,
    Subscribed,
}

/// <summary>Fila de la vista de bajas (opt-outs): el consentimiento de un cliente por número.</summary>
public sealed record SmsOptOutSummaryResponse(
    Guid CustomerId,
    string PhoneE164,
    SmsOptOutStatus Status,
    string? LastKeyword,
    DateTime? OptedOutAtUtc,
    DateTime? OptedInAtUtc,
    DateTime UpdatedAtUtc
);
