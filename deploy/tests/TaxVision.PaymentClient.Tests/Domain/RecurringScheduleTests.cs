using TaxVision.PaymentClient.Domain.Recurring;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Tests.Domain;

public sealed class RecurringScheduleTests
{
    [Fact]
    public void ChargeIdempotencyKey_ChangesAfterRetryPending()
    {
        var planId = Guid.NewGuid();
        var schedule = RecurringSchedule.Create(
            planId,
            Guid.NewGuid(),
            DateTime.UtcNow,
            Money.Create(1999, "USD").Value
        );

        Assert.Equal($"recurring:{planId:N}:{schedule.Id:N}:attempt:0", schedule.ChargeIdempotencyKey(planId));

        schedule.MarkProcessing();
        schedule.MarkRetryPending(DateTime.UtcNow.AddHours(1), "declined");

        Assert.Equal($"recurring:{planId:N}:{schedule.Id:N}:attempt:1", schedule.ChargeIdempotencyKey(planId));
    }
}
