using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Credentials.Administration;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Estado de entrega del link (04 §4.3): Sent, Delivered, Bounced y Failed solo con lo que dice Notification.</summary>
public sealed class ResetLinkDeliveryTests
{
    private static readonly DateTime Now = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Without_a_log_a_fresh_link_is_still_being_sent() =>
        Assert.Equal(
            ResetLinkDelivery.Queued,
            ResetLinkDeliveryResolver.Resolve(NotificationEmailLookup.NotFound, Now.AddMinutes(-2), Now)
        );

    [Fact]
    public void Without_a_log_an_old_link_is_unknown() =>
        Assert.Equal(
            ResetLinkDelivery.Unknown,
            ResetLinkDeliveryResolver.Resolve(NotificationEmailLookup.NotFound, Now.AddHours(-1), Now)
        );

    [Theory]
    [InlineData("Skipped")]
    [InlineData("Whatever")]
    public void An_unexpected_status_is_unknown(string status) =>
        Assert.Equal(
            ResetLinkDelivery.Unknown,
            ResetLinkDeliveryResolver.Resolve(NotificationEmailLookup.Found(status), Now, Now)
        );
}
