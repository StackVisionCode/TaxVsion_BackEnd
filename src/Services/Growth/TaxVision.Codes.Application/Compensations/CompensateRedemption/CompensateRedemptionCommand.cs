using TaxVision.Codes.Domain.Compensations;

namespace TaxVision.Codes.Application.Compensations.CompensateRedemption;

public sealed record CompensateRedemptionCommand(
    Guid TenantId,
    Guid RedemptionId,
    CodeCompensationType Type,
    long AdjustmentAmountCents,
    string Currency,
    string Reason,
    Guid SourceEventId,
    string IdempotencyKey
);
