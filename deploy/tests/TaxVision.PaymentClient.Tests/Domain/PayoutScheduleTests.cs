using TaxVision.PaymentClient.Domain.Payouts;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Tests.Domain;

public sealed class PayoutScheduleTests
{
    private static readonly DateTime NowUtc = DateTime.UtcNow;

    [Fact]
    public void Create_Weekly_without_an_anchor_fails()
    {
        var result = PayoutSchedule.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PayoutFrequency.Weekly,
            null,
            "USD",
            Guid.Empty,
            NowUtc
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PayoutSchedule.InvalidAnchor", result.Error.Code);
    }

    [Fact]
    public void Create_Monthly_with_an_out_of_range_anchor_fails()
    {
        var result = PayoutSchedule.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PayoutFrequency.Monthly,
            32,
            "USD",
            Guid.Empty,
            NowUtc
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PayoutSchedule.InvalidAnchor", result.Error.Code);
    }

    [Fact]
    public void Create_Manual_with_an_anchor_fails()
    {
        var result = PayoutSchedule.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PayoutFrequency.Manual,
            1,
            "USD",
            Guid.Empty,
            NowUtc
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PayoutSchedule.InvalidAnchor", result.Error.Code);
    }

    [Fact]
    public void Create_Weekly_with_a_valid_anchor_succeeds()
    {
        var result = PayoutSchedule.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PayoutFrequency.Weekly,
            5,
            "USD",
            Guid.Empty,
            NowUtc
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.Anchor);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public void UpdateFrequency_to_Daily_clears_the_anchor_requirement()
    {
        var schedule = PayoutSchedule
            .Create(Guid.NewGuid(), Guid.NewGuid(), PayoutFrequency.Weekly, 3, "USD", Guid.Empty, NowUtc)
            .Value;

        var result = schedule.UpdateFrequency(PayoutFrequency.Daily, null, Guid.Empty, NowUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(PayoutFrequency.Daily, schedule.Frequency);
        Assert.Null(schedule.Anchor);
    }

    [Fact]
    public void RecordPayoutPaid_appends_an_item()
    {
        var schedule = PayoutSchedule
            .Create(Guid.NewGuid(), Guid.NewGuid(), PayoutFrequency.Manual, null, "USD", Guid.Empty, NowUtc)
            .Value;

        schedule.RecordPayoutPaid("po_123", Money.Create(5000, "USD").Value, NowUtc);

        Assert.Single(schedule.Items);
        Assert.Equal(PayoutScheduleItemStatus.Paid, schedule.Items.Single().Status);
    }

    [Fact]
    public void RecordPayoutFailed_appends_an_item_with_a_reason()
    {
        var schedule = PayoutSchedule
            .Create(Guid.NewGuid(), Guid.NewGuid(), PayoutFrequency.Manual, null, "USD", Guid.Empty, NowUtc)
            .Value;

        schedule.RecordPayoutFailed("po_124", Money.Create(5000, "USD").Value, "insufficient_funds", NowUtc);

        var item = schedule.Items.Single();
        Assert.Equal(PayoutScheduleItemStatus.Failed, item.Status);
        Assert.Equal("insufficient_funds", item.FailureReason);
    }
}
