using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.Recurring;

namespace TaxVision.PaymentClient.Application.Recurring.Queries;

public static class GetTenantRecurringPaymentByIdHandler
{
    public static async Task<Result<TenantRecurringPaymentResponse>> Handle(
        GetTenantRecurringPaymentByIdQuery query,
        ITenantRecurringPaymentRepository plans,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByIdAsync(query.TenantRecurringPaymentId, query.TenantId, ct);
        if (plan is null)
            return Result.Failure<TenantRecurringPaymentResponse>(
                new Error("TenantRecurringPayment.NotFound", "TenantRecurringPayment does not exist.")
            );

        return Result.Success(Map(plan));
    }

    internal static TenantRecurringPaymentResponse Map(TenantRecurringPayment plan) =>
        new(
            plan.Id,
            plan.TaxpayerId,
            plan.ProviderCode.ToString(),
            plan.Amount.AmountCents,
            plan.Amount.Currency,
            plan.Purpose.Kind.ToString(),
            plan.Purpose.ExternalReferenceId,
            plan.BillingCycle.ToString(),
            plan.StartDate,
            plan.EndDate,
            plan.MaxExecutions,
            plan.Status.ToString(),
            plan.NextExecutionDate,
            plan.ExecutionCount,
            plan.FailureCount,
            plan.Schedules.Select(s => new RecurringScheduleResponse(
                    s.Id,
                    s.ScheduledDate,
                    s.Status.ToString(),
                    s.Amount.AmountCents,
                    s.Amount.Currency,
                    s.TenantPaymentId,
                    s.RetryCount,
                    s.NextRetryAtUtc
                ))
                .OrderBy(s => s.ScheduledDate)
                .ToList()
        );
}
