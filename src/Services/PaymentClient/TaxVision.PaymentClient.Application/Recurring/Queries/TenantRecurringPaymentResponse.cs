namespace TaxVision.PaymentClient.Application.Recurring.Queries;

public sealed record RecurringScheduleResponse(
    Guid Id,
    DateTime ScheduledDate,
    string Status,
    long AmountCents,
    string Currency,
    Guid? TenantPaymentId,
    int RetryCount,
    DateTime? NextRetryAtUtc
);

public sealed record TenantRecurringPaymentResponse(
    Guid Id,
    Guid TaxpayerId,
    string ProviderCode,
    long AmountCents,
    string Currency,
    string PurposeKind,
    string? PurposeExternalReferenceId,
    string BillingCycle,
    DateTime StartDate,
    DateTime? EndDate,
    int? MaxExecutions,
    string Status,
    DateTime? NextExecutionDate,
    int ExecutionCount,
    int FailureCount,
    IReadOnlyList<RecurringScheduleResponse> Schedules
);
