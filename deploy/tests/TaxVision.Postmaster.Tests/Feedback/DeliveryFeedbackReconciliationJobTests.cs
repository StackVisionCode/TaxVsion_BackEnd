using TaxVision.Postmaster.Api.Jobs;
using TaxVision.Postmaster.Infrastructure.Feedback;

namespace TaxVision.Postmaster.Tests.Feedback;

/// <summary>Qué le pide el job a cada handler en una vuelta, según las opciones.</summary>
public sealed class DeliveryFeedbackReconciliationJobTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Engagement_reads_the_lookback_window_up_to_now_with_the_cap()
    {
        var command = DeliveryFeedbackReconciliationJob.BuildEngagementCommand(
            new DeliveryFeedbackReconciliationOptions
            {
                EngagementLookbackHours = 48,
                MaxEngagementEventsPerRun = 2000,
            },
            Now
        );

        Assert.NotNull(command);
        Assert.Equal(Now.AddHours(-48), command.FromUtc);
        Assert.Equal(Now, command.ToUtc);
        Assert.Equal(2000, command.MaxEvents);
    }

    [Fact]
    public void Engagement_is_skipped_when_disabled()
    {
        Assert.Null(
            DeliveryFeedbackReconciliationJob.BuildEngagementCommand(
                new DeliveryFeedbackReconciliationOptions { EngagementEnabled = false },
                Now
            )
        );
    }

    [Fact]
    public void Defaults_turn_engagement_on_with_48_hours_and_2000_events()
    {
        var defaults = new DeliveryFeedbackReconciliationOptions();

        Assert.True(defaults.EngagementEnabled);
        Assert.Equal(48, defaults.EngagementLookbackHours);
        Assert.Equal(2000, defaults.MaxEngagementEventsPerRun);
    }

    [Fact]
    public void Delivery_window_is_unchanged()
    {
        var command = DeliveryFeedbackReconciliationJob.BuildDeliveryCommand(
            new DeliveryFeedbackReconciliationOptions(),
            Now
        );

        Assert.Equal(Now.AddHours(-72), command.SentFromUtc);
        Assert.Equal(Now.AddHours(-6), command.SentToUtc);
        Assert.Equal(50, command.MaxRecipients);
    }
}
