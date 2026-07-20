namespace TaxVision.Codes.Application.Reservations.ReserveCode;

public sealed record ReserveCodeCommand(
    Guid TenantId,
    Guid QuoteId,
    string PaymentSource,
    Guid PaymentId,
    string IdempotencyKey,
    int TtlSeconds
);
