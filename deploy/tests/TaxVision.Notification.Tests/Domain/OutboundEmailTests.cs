using TaxVision.Notification.Domain.Emailing.Sending;

namespace TaxVision.Notification.Tests.Domain;

public sealed class OutboundEmailTests
{
    [Fact]
    public void Message_requires_a_to_recipient()
    {
        var result = OutboundEmailMessage.Create(
            Guid.NewGuid(), "Subject", "<p>Hi</p>", "Hi", EmailPriority.Normal,
            [("cc@example.com", EmailRecipientKind.Cc, null)],
            "[]", null, null, null, null
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Email.Recipients", result.Error.Code);
    }

    [Fact]
    public void Invalid_recipient_address_is_rejected()
    {
        var result = OutboundEmailMessage.Create(
            Guid.NewGuid(), "Subject", "<p>Hi</p>", "Hi", EmailPriority.Normal,
            [("not-an-email", EmailRecipientKind.To, null)],
            "[]", null, null, null, null
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Email.Recipients", result.Error.Code);
    }

    [Fact]
    public void Sent_message_records_provider_and_clears_error()
    {
        var message = CreateMessage();
        message.MarkFailed("temporary error");

        message.MarkSent("Smtp", Guid.NewGuid());

        Assert.Equal(EmailStatus.Sent, message.Status);
        Assert.Null(message.Error);
        Assert.NotNull(message.SentAtUtc);
        Assert.Equal("Smtp", message.ProviderType);
    }

    [Fact]
    public void Queued_message_can_deliver_but_sent_cannot()
    {
        var message = CreateMessage();
        Assert.True(message.CanDeliver());

        message.MarkSent("Smtp", null);
        Assert.False(message.CanDeliver());
    }

    [Fact]
    public void Open_tracking_is_first_time_only()
    {
        var message = CreateMessage();

        Assert.True(message.MarkOpened());
        Assert.False(message.MarkOpened());
        Assert.NotNull(message.OpenedAtUtc);
    }

    [Fact]
    public void Delivered_only_advances_from_sent()
    {
        var message = CreateMessage();
        message.MarkSent("Smtp", null);

        message.MarkDelivered();

        Assert.Equal(EmailStatus.Delivered, message.Status);
        Assert.NotNull(message.DeliveredAtUtc);
    }

    private static OutboundEmailMessage CreateMessage() =>
        OutboundEmailMessage.Create(
            Guid.NewGuid(), "Subject", "<p>Hi</p>", "Hi", EmailPriority.Normal,
            [("to@example.com", EmailRecipientKind.To, "To")],
            "[]", null, null, null, "correlation"
        ).Value;
}
