using TaxVision.Correspondence.Domain.Delivery;

namespace TaxVision.Correspondence.Tests.Domain;

public sealed class DeliveryNoticeTests
{
    private static DeliveryNotice Create(
        DeliveryOutcome outcome = DeliveryOutcome.NotDelivered,
        string? category = "InvalidAddress",
        Guid? reportAccountId = null,
        string? reportProviderMessageId = "gmail-msg-1",
        Guid? customerId = null,
        string recipient = "  Maria@X.com "
    ) =>
        DeliveryNotice
            .Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                customerId,
                Guid.NewGuid(),
                recipient,
                outcome,
                category,
                "5.1.1",
                "550 5.1.1 user unknown",
                suppressed: true,
                reportAccountId ?? Guid.NewGuid(),
                reportProviderMessageId,
                DateTime.UtcNow.AddMinutes(-5),
                DateTime.UtcNow
            )
            .Value;

    [Fact]
    public void A_not_delivered_notice_is_unread_and_requires_action()
    {
        var notice = Create();

        Assert.False(notice.IsRead);
        Assert.True(notice.RequiresAction);
        Assert.Equal("maria@x.com", notice.Recipient);
        Assert.Equal(DeliveryReasonCodes.InvalidAddress, notice.ReasonCode);
        Assert.True(notice.CanViewOriginal);
    }

    [Fact]
    public void A_delayed_notice_is_born_read_and_never_goes_back_to_unread()
    {
        var notice = Create(DeliveryOutcome.Delayed, category: null);

        Assert.True(notice.IsRead);
        Assert.False(notice.RequiresAction);
        Assert.False(notice.MarkUnread());
        Assert.True(notice.IsRead);
        Assert.Equal(DeliveryReasonCodes.Delayed, notice.ReasonCode);
    }

    [Fact]
    public void Read_state_changes_are_idempotent()
    {
        var notice = Create();

        Assert.True(notice.MarkRead(DateTime.UtcNow));
        Assert.False(notice.MarkRead(DateTime.UtcNow));
        Assert.True(notice.MarkUnread());
        Assert.False(notice.MarkUnread());
        Assert.Null(notice.ReadAtUtc);
    }

    [Fact]
    public void A_webhook_result_or_an_oversized_provider_id_cannot_show_the_original()
    {
        var webhook = DeliveryNotice
            .Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                Guid.NewGuid(),
                "a@b.com",
                DeliveryOutcome.NotDelivered,
                "Rejected",
                null,
                null,
                false,
                null,
                null,
                DateTime.UtcNow,
                DateTime.UtcNow
            )
            .Value;
        var oversized = Create(
            reportProviderMessageId: new string('x', DeliveryNotice.ReportProviderMessageIdMaxLength + 1)
        );

        Assert.False(webhook.CanViewOriginal);
        Assert.Null(webhook.ReportAccountId);
        Assert.False(oversized.CanViewOriginal);
        Assert.Null(oversized.ReportProviderMessageId);
    }

    [Theory]
    [InlineData(DeliveryOutcome.NotDelivered, "DomainNotFound", DeliveryReasonCodes.DomainNotFound)]
    [InlineData(DeliveryOutcome.NotDelivered, "MailboxFull", DeliveryReasonCodes.MailboxFull)]
    [InlineData(DeliveryOutcome.NotDelivered, "Rejected", DeliveryReasonCodes.Rejected)]
    [InlineData(DeliveryOutcome.NotDelivered, "TooLarge", DeliveryReasonCodes.TooLarge)]
    [InlineData(DeliveryOutcome.Delayed, "Delayed", DeliveryReasonCodes.Delayed)]
    [InlineData(DeliveryOutcome.NotDelivered, "Other", DeliveryReasonCodes.Other)]
    [InlineData(DeliveryOutcome.NotDelivered, null, DeliveryReasonCodes.Other)]
    [InlineData(DeliveryOutcome.NotDelivered, "SomethingNew", DeliveryReasonCodes.Other)]
    public void The_reason_code_is_stable_per_category(DeliveryOutcome outcome, string? category, string expected) =>
        Assert.Equal(expected, DeliveryReasonCodes.From(outcome, category));

    [Fact]
    public void A_delivered_result_or_an_invalid_customer_is_rejected()
    {
        var delivered = DeliveryNotice.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            "a@b.com",
            DeliveryOutcome.Delivered,
            null,
            null,
            null,
            false,
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow
        );
        var emptyCustomer = DeliveryNotice.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.Empty,
            Guid.NewGuid(),
            "a@b.com",
            DeliveryOutcome.NotDelivered,
            null,
            null,
            null,
            false,
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow
        );

        Assert.Equal("DeliveryNotice.DeliveredHasNoNotice", delivered.Error.Code);
        Assert.Equal("DeliveryNotice.CustomerIdInvalid", emptyCustomer.Error.Code);
    }

    [Fact]
    public void Long_technical_fields_are_truncated()
    {
        var notice = DeliveryNotice
            .Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                Guid.NewGuid(),
                "a@b.com",
                DeliveryOutcome.NotDelivered,
                "Other",
                "5.1.1",
                new string('d', 5000),
                false,
                null,
                null,
                DateTime.UtcNow,
                DateTime.UtcNow
            )
            .Value;

        Assert.Equal(DeliveryNotice.DiagnosticMaxLength, notice.Diagnostic!.Length);
    }
}
