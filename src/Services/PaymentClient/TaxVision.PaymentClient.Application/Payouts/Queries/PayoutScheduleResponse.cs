namespace TaxVision.PaymentClient.Application.Payouts.Queries;

public sealed record PayoutScheduleItemResponse(
    Guid Id, string ProviderPayoutReference, long AmountCents, string Currency, string Status, string? FailureReason, DateTime OccurredAtUtc);

public sealed record PayoutScheduleResponse(
    Guid Id,
    string Frequency,
    int? Anchor,
    string Currency,
    IReadOnlyList<PayoutScheduleItemResponse> Items
);
