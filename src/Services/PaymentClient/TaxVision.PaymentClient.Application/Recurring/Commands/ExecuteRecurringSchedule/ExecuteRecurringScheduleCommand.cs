namespace TaxVision.PaymentClient.Application.Recurring.Commands.ExecuteRecurringSchedule;

/// <summary>Dispatchado por <c>TenantRecurringExecutionJob</c> (schedules <c>Pending</c>
/// vencidos) y <c>TenantRecurringRetryJob</c> (schedules <c>RetryPending</c> vencidos) — no se
/// expone vía HTTP, mismo patrón que <c>RetrySaaSPaymentCommand</c> de PaymentApp (dunning es
/// job-only).</summary>
public sealed record ExecuteRecurringScheduleCommand(Guid TenantId, Guid TenantRecurringPaymentId, Guid ScheduleId);
