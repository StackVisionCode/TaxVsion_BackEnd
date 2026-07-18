using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;

namespace TaxVision.PaymentClient.Application.Payouts.Queries;

public static class GetPayoutScheduleHandler
{
    public static async Task<Result<PayoutScheduleResponse>> Handle(
        GetPayoutScheduleQuery query, IPayoutScheduleRepository schedules, CancellationToken ct)
    {
        var schedule = await schedules.GetByTenantAsync(query.TenantId, ct);
        if (schedule is null)
            return Result.Failure<PayoutScheduleResponse>(new Error("PayoutSchedule.NotFound", "PayoutSchedule does not exist."));

        var items = schedule.Items
            .Select(item => new PayoutScheduleItemResponse(
                item.Id, item.ProviderPayoutReference, item.Amount.AmountCents, item.Amount.Currency, item.Status.ToString(), item.FailureReason, item.OccurredAtUtc))
            .OrderByDescending(item => item.OccurredAtUtc)
            .ToList();

        return Result.Success(new PayoutScheduleResponse(schedule.Id, schedule.Frequency.ToString(), schedule.Anchor, schedule.Currency, items));
    }
}
