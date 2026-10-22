using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Tests;

public sealed class ChargeFailureConsequencesTests
{
    [Theory]
    [InlineData(
        "SubscriptionSuspended",
        null,
        "your subscription has been suspended and your team can no longer sign in"
    )]
    [InlineData(
        "SeatSuspended",
        null,
        "the user seat has been suspended and the person assigned to it can no longer sign in"
    )]
    [InlineData("AddOnSuspended", "Email add-on", "the Email add-on has been paused")]
    [InlineData("AddOnSuspended", null, "the add-on has been paused")]
    [InlineData(null, null, "the service will remain on hold until the payment is completed")]
    public void Says_what_changed_after_the_last_attempt(string? consequence, string? item, string expected)
    {
        Assert.Equal(expected, ChargeFailureConsequences.Describe(consequence, item));
    }
}
