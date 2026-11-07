using TaxVision.Notification.Domain.Emailing.Sending;

namespace TaxVision.Notification.Tests.Domain;

public sealed class OutboundEmailTests
{
    [Fact]
    public void Message_requires_a_to_recipient()
    {
        var result = OutboundEmailMessage.Create(
            Guid.NewGuid(),
            "Subject",
            "<p>Hi</p>",
            "Hi",
            EmailPriority.Normal,
            [("cc@example.com", EmailRecipientKind.Cc, null)],
            "[]",
            null,
            null,
            null,
            null
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Email.Recipients", result.Error.Code);
    }

    [Fact]
    public void Invalid_recipient_address_is_rejected()
    {
        var result = OutboundEmailMessage.Create(
            Guid.NewGuid(),
            "Subject",
            "<p>Hi</p>",
            "Hi",
            EmailPriority.Normal,
            [("not-an-email", EmailRecipientKind.To, null)],
            "[]",
            null,
            null,
            null,
            null
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

    [Fact]
    public void Mark_sent_from_bounced_is_rejected()
    {
        var message = CreateMessage();
        message.MarkSending();
        message.MarkBounced("Permanent: unknown user");

        var result = message.MarkSent("Postmaster", null);

        Assert.True(result.IsFailure);
        Assert.Equal("Email.InvalidTransition", result.Error.Code);
        Assert.Equal(EmailStatus.Bounced, message.Status);
        Assert.Null(message.SentAtUtc);
    }

    [Fact]
    public void Mark_sent_from_delivered_is_rejected()
    {
        var message = CreateMessage();
        message.MarkSent("Postmaster", null);
        message.MarkDelivered();

        var result = message.MarkSent("Postmaster", null);

        Assert.True(result.IsFailure);
        Assert.Equal(EmailStatus.Delivered, message.Status);
    }

    [Fact]
    public void Mark_bounced_twice_keeps_the_first_bounce()
    {
        var message = CreateMessage();
        message.MarkSending();
        message.MarkBounced("first");
        var bouncedAt = message.BouncedAtUtc;

        message.MarkBounced("second");

        Assert.Equal(bouncedAt, message.BouncedAtUtc);
        Assert.Single(message.DeliveryLogs, l => l.Status == EmailStatus.Bounced);
    }

    [Fact]
    public void Mark_bounced_from_failed_is_rejected()
    {
        var message = CreateMessage();
        message.MarkSending();
        message.MarkFailed("smtp down");

        var result = message.MarkBounced("late");

        Assert.True(result.IsFailure);
        Assert.Equal(EmailStatus.Failed, message.Status);
        Assert.Null(message.BouncedAtUtc);
    }

    [Fact]
    public void Late_bounce_after_delivered_is_accepted()
    {
        var message = CreateMessage();
        message.MarkSent("Postmaster", null);
        message.MarkDelivered();

        message.MarkBounced("late");

        Assert.Equal(EmailStatus.Bounced, message.Status);
    }

    [Fact]
    public void Late_failure_keeps_the_bounce()
    {
        var message = CreateMessage();
        message.MarkSending();
        message.MarkBounced("Permanent: unknown user");

        var result = message.MarkFailed("late failure");

        Assert.True(result.IsFailure);
        Assert.Equal("Email.InvalidTransition", result.Error.Code);
        Assert.Equal(EmailStatus.Bounced, message.Status);
        Assert.Null(message.Error);
        Assert.Equal(0, message.RetryCount);
        Assert.DoesNotContain(message.DeliveryLogs, l => l.Status == EmailStatus.Failed);
    }

    [Fact]
    public void Late_failure_keeps_the_sent_and_delivered_states()
    {
        var sent = CreateMessage();
        sent.MarkSent("Postmaster", null);
        var delivered = CreateMessage();
        delivered.MarkSent("Postmaster", null);
        delivered.MarkDelivered();

        sent.MarkFailed("late failure");
        delivered.MarkFailed("late failure");

        Assert.Equal(EmailStatus.Sent, sent.Status);
        Assert.Equal(EmailStatus.Delivered, delivered.Status);
    }

    [Fact]
    public void Repeated_failure_counts_once()
    {
        var message = CreateMessage();
        message.MarkSending();
        message.MarkFailed("first");

        message.MarkFailed("second");

        Assert.Equal("first", message.Error);
        Assert.Equal(1, message.RetryCount);
    }

    [Fact]
    public void Failure_is_accepted_from_queued_and_sending()
    {
        var queued = CreateMessage();
        var sending = CreateMessage();
        sending.MarkSending();

        queued.MarkFailed("smtp down");
        sending.MarkFailed("smtp down");

        Assert.Equal(EmailStatus.Failed, queued.Status);
        Assert.Equal(EmailStatus.Failed, sending.Status);
        Assert.Equal(1, sending.RetryCount);
    }

    [Fact]
    public void A_retried_message_can_fail_again()
    {
        var message = CreateMessage();
        message.MarkSending();
        message.MarkFailed("first");
        message.MarkSending();

        message.MarkFailed("second");

        Assert.Equal("second", message.Error);
        Assert.Equal(2, message.RetryCount);
    }

    private static OutboundEmailMessage CreateMessage() =>
        OutboundEmailMessage
            .Create(
                Guid.NewGuid(),
                "Subject",
                "<p>Hi</p>",
                "Hi",
                EmailPriority.Normal,
                [("to@example.com", EmailRecipientKind.To, "To")],
                "[]",
                null,
                null,
                null,
                "correlation"
            )
            .Value;
}
