using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Tests.Domain;

public sealed class SentMessageTests
{
    private static SentMessage CreateValidMessage(EmailStream stream = EmailStream.Transactional) =>
        SentMessage
            .Queue(
                tenantId: Guid.NewGuid(),
                idempotencyKey: Guid.NewGuid().ToString("N"),
                subject: "Password reset",
                fromAddress: "no-reply@taxvision.com",
                stream: stream,
                providerCode: "system-smtp",
                notificationLogId: Guid.NewGuid(),
                correlationId: "corr-1",
                fromDisplayName: "TaxVision",
                replyTo: null,
                templateKey: "auth.password_reset",
                queuedAtUtc: DateTime.UtcNow
            )
            .Value;

    [Fact]
    public void Queue_creates_message_with_correct_status()
    {
        var result = SentMessage.Queue(
            tenantId: Guid.NewGuid(),
            idempotencyKey: "key-1",
            subject: "Hello",
            fromAddress: "FROM@Example.com",
            stream: EmailStream.Transactional,
            providerCode: "system-smtp",
            notificationLogId: null,
            correlationId: null,
            fromDisplayName: null,
            replyTo: null,
            templateKey: null,
            queuedAtUtc: DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        var message = result.Value;
        Assert.Equal(SentMessageStatus.Queued, message.Status);
        Assert.Equal("from@example.com", message.FromAddress); // normalizado lowercase
        Assert.Single(message.Events);
        Assert.Equal(SentMessageEventType.Queued, message.Events.First().EventType);
    }

    [Fact]
    public void Queue_rejects_empty_idempotency_key()
    {
        var result = SentMessage.Queue(
            tenantId: Guid.NewGuid(),
            idempotencyKey: "",
            subject: "Hello",
            fromAddress: "from@example.com",
            stream: EmailStream.Transactional,
            providerCode: "system-smtp",
            notificationLogId: null,
            correlationId: null,
            fromDisplayName: null,
            replyTo: null,
            templateKey: null,
            queuedAtUtc: DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SentMessage.IdempotencyKey", result.Error.Code);
    }

    [Fact]
    public void Queue_rejects_invalid_from_address()
    {
        var result = SentMessage.Queue(
            tenantId: Guid.NewGuid(),
            idempotencyKey: "key-1",
            subject: "Hello",
            fromAddress: "not-an-email",
            stream: EmailStream.Transactional,
            providerCode: "system-smtp",
            notificationLogId: null,
            correlationId: null,
            fromDisplayName: null,
            replyTo: null,
            templateKey: null,
            queuedAtUtc: DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SentMessage.FromAddress", result.Error.Code);
    }

    [Fact]
    public void AddRecipient_prevents_duplicates()
    {
        var message = CreateValidMessage();
        var first = message.AddRecipient("customer@example.com", RecipientType.To, "Customer");
        Assert.True(first.IsSuccess);

        var duplicate = message.AddRecipient("Customer@Example.com", RecipientType.To, null);

        Assert.True(duplicate.IsFailure);
        Assert.Equal("SentMessage.DuplicateRecipient", duplicate.Error.Code);
        Assert.Single(message.Recipients);
    }

    [Fact]
    public void AddRecipient_allows_same_address_with_different_type()
    {
        var message = CreateValidMessage();
        message.AddRecipient("customer@example.com", RecipientType.To, null);

        var result = message.AddRecipient("customer@example.com", RecipientType.Cc, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, message.Recipients.Count);
    }

    [Fact]
    public void MarkAsSent_records_event_and_transitions_pending_recipients()
    {
        var message = CreateValidMessage();
        message.AddRecipient("customer@example.com", RecipientType.To, null);
        message.MarkAsSending();

        var result = message.MarkAsSent("provider-msg-123", DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(SentMessageStatus.Sent, message.Status);
        Assert.NotNull(message.SentAtUtc);
        Assert.Contains(message.Events, e => e.EventType == SentMessageEventType.Sent);
        var recipient = message.Recipients.First();
        Assert.Equal(RecipientStatus.Sent, recipient.Status);
        Assert.Equal("provider-msg-123", recipient.ProviderMessageId);
    }

    [Fact]
    public void MarkAsSent_from_invalid_state_throws_via_result_failure()
    {
        var message = CreateValidMessage(); // still Queued, never MarkAsSending()

        var result = message.MarkAsSent("id", DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("SentMessage.InvalidTransition", result.Error.Code);
        Assert.Equal(SentMessageStatus.Queued, message.Status);
    }

    [Fact]
    public void MarkAsFailed_allowed_from_queued_and_sending()
    {
        var fromQueued = CreateValidMessage();
        var r1 = fromQueued.MarkAsFailed("SystemProviderMissing", DateTime.UtcNow);
        Assert.True(r1.IsSuccess);
        Assert.Equal(SentMessageStatus.Failed, fromQueued.Status);

        var fromSending = CreateValidMessage();
        fromSending.MarkAsSending();
        var r2 = fromSending.MarkAsFailed("SmtpTimeout", DateTime.UtcNow);
        Assert.True(r2.IsSuccess);
        Assert.Equal(SentMessageStatus.Failed, fromSending.Status);
    }

    [Fact]
    public void MarkAsSuppressed_only_from_queued()
    {
        var message = CreateValidMessage();
        message.MarkAsSending();

        var result = message.MarkAsSuppressed("hard bounce history", DateTime.UtcNow);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void MarkAsProviderNotConfigured_transitions_from_queued()
    {
        var message = CreateValidMessage();

        var result = message.MarkAsProviderNotConfigured(DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(SentMessageStatus.ProviderNotConfigured, message.Status);
    }

    [Fact]
    public void MarkRecipientSuppressed_changes_only_that_recipient()
    {
        var message = CreateValidMessage();
        var recipient = message.AddRecipient("customer@example.com", RecipientType.To, null).Value;

        var result = message.MarkRecipientSuppressed(recipient.Id, "Address in suppression list.", DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        // El mensaje no cambia solo: el caller decide con MarkAsSuppressed si todos quedaron suprimidos.
        Assert.Equal(SentMessageStatus.Queued, message.Status);
        Assert.Equal(RecipientStatus.Suppressed, recipient.Status);
        Assert.Contains(
            message.Events,
            e => e.EventType == SentMessageEventType.Suppressed && e.RecipientId == recipient.Id
        );
    }

    [Fact]
    public void MarkRecipientSuppressed_for_unknown_recipient_fails()
    {
        var message = CreateValidMessage();

        var result = message.MarkRecipientSuppressed(Guid.NewGuid(), "reason", DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("SentMessage.RecipientNotFound", result.Error.Code);
    }

    [Fact]
    public void MarkRecipientRejectedAtSend_keeps_the_recipient_out_of_MarkAsSent()
    {
        var message = CreateValidMessage();
        var accepted = message.AddRecipient("ok@example.com", RecipientType.To, null).Value;
        var rejected = message.AddRecipient("bad@example.com", RecipientType.To, null).Value;
        message.MarkAsSending();

        message.MarkRecipientRejectedAtSend(rejected.Id, "550 5.1.1 user unknown", DateTime.UtcNow);
        message.MarkAsSent("msg-id", DateTime.UtcNow);

        Assert.Equal(RecipientStatus.Sent, accepted.Status);
        Assert.Equal(DeliveryOutcome.Accepted, accepted.Outcome);
        Assert.Equal(RecipientStatus.Failed, rejected.Status);
        Assert.Equal(DeliveryOutcome.Rejected, rejected.Outcome);
        Assert.Equal("550 5.1.1 user unknown", rejected.ErrorReason);
    }

    [Fact]
    public void MarkRecipientRejectedAtSend_outside_sending_fails()
    {
        var message = CreateValidMessage();
        var recipient = message.AddRecipient("bad@example.com", RecipientType.To, null).Value;

        var result = message.MarkRecipientRejectedAtSend(recipient.Id, "550", DateTime.UtcNow);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void RecordInlineAssets_accepts_assets_within_total_limit()
    {
        var message = CreateValidMessage();
        var assets = new[] { InlineAsset.Create("logo", Guid.NewGuid(), "image/png", 100 * 1024).Value };

        var result = message.RecordInlineAssets(assets);

        Assert.True(result.IsSuccess);
        Assert.Single(message.InlineAssets);
    }

    [Fact]
    public void RecordInlineAssets_rejects_assets_over_5MB_total()
    {
        var message = CreateValidMessage();
        var assets = Enumerable
            .Range(0, 26)
            .Select(i => InlineAsset.Create($"logo-{i}", Guid.NewGuid(), "image/png", 200 * 1024).Value)
            .ToArray(); // 26 * 200KB = 5.08MB > 5MB

        var result = message.RecordInlineAssets(assets);

        Assert.True(result.IsFailure);
        Assert.Equal("SentMessage.InlineAssetsTooLarge", result.Error.Code);
    }

    [Fact]
    public void RecordRenderedChecksum_and_RecordMimeSize_set_values()
    {
        var message = CreateValidMessage();

        message.RecordRenderedChecksum("abc123");
        message.RecordMimeSize(4096);

        Assert.Equal("abc123", message.RenderedHtmlChecksum);
        Assert.Equal(4096, message.MimeSize);
    }

    [Fact]
    public void Queue_persists_correspondence_draft_id_and_threading_fields()
    {
        var draftId = Guid.NewGuid();

        var result = SentMessage.Queue(
            tenantId: Guid.NewGuid(),
            idempotencyKey: "key-1",
            subject: "Re: Tax question",
            fromAddress: "office@example.com",
            stream: EmailStream.Transactional,
            providerCode: "gmail",
            notificationLogId: null,
            correlationId: null,
            fromDisplayName: null,
            replyTo: null,
            templateKey: null,
            queuedAtUtc: DateTime.UtcNow,
            correspondenceDraftId: draftId,
            inReplyToInternetMessageId: "<abc@example.com>",
            references: ["<abc@example.com>", "<def@example.com>"]
        );

        Assert.True(result.IsSuccess);
        var message = result.Value;
        Assert.Equal(draftId, message.CorrespondenceDraftId);
        Assert.Equal("<abc@example.com>", message.InReplyToInternetMessageId);
        Assert.Equal(["<abc@example.com>", "<def@example.com>"], message.References);
    }

    [Fact]
    public void Queue_without_correspondence_fields_leaves_them_null_or_empty()
    {
        var message = CreateValidMessage();

        Assert.Null(message.CorrespondenceDraftId);
        Assert.Null(message.InReplyToInternetMessageId);
        Assert.Empty(message.References);
        Assert.Empty(message.Attachments);
        Assert.Null(message.ProviderThreadId);
    }

    [Fact]
    public void RecordAttachments_replaces_the_attachment_set_without_a_size_cap()
    {
        var message = CreateValidMessage();
        var attachments = Enumerable
            .Range(0, 5)
            .Select(i =>
                OutboundAttachmentRef.Create(Guid.NewGuid(), $"file-{i}.pdf", "application/pdf", 10 * 1024 * 1024).Value
            )
            .ToArray(); // 50MB total — no cap enforced here (D3 Compose §11.3/§12)

        var result1 = message.AddRecipient("customer@example.com", RecipientType.To, null);
        Assert.True(result1.IsSuccess);
        message.RecordAttachments(attachments);

        Assert.Equal(5, message.Attachments.Count);

        message.RecordAttachments([attachments[0]]);
        Assert.Single(message.Attachments);
    }

    [Fact]
    public void MarkAsSent_records_provider_thread_id_when_provided()
    {
        var message = CreateValidMessage();
        message.MarkAsSending();

        var result = message.MarkAsSent("provider-msg-1", DateTime.UtcNow, "thread-99");

        Assert.True(result.IsSuccess);
        Assert.Equal("thread-99", message.ProviderThreadId);
    }

    private static readonly DateTime T0 = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    private static (SentMessage Message, SentMessageRecipient Recipient) SentToOne()
    {
        var message = CreateValidMessage();
        var recipient = message.AddRecipient("customer@example.com", RecipientType.To, null).Value;
        message.MarkAsSending();
        message.MarkAsSent("msg-id", T0);
        return (message, recipient);
    }

    private static DeliveryFeedback Outcome(DeliveryOutcome outcome, DateTime at, string? diagnostic = null) =>
        DeliveryFeedback
            .ForOutcome(outcome, at, FeedbackSource.Smtp2Go, Guid.NewGuid().ToString("N"), diagnostic)
            .Value;

    private static DeliveryFeedback Signal(EngagementSignal signal, DateTime at) =>
        DeliveryFeedback.ForSignal(signal, at, FeedbackSource.Smtp2Go, Guid.NewGuid().ToString("N")).Value;

    [Fact]
    public void MarkAsSent_sets_the_Accepted_outcome()
    {
        var (_, recipient) = SentToOne();

        Assert.Equal(DeliveryOutcome.Accepted, recipient.Outcome);
    }

    [Fact]
    public void ApplyDeliveryFeedback_late_event_does_not_go_back_but_stays_in_the_history()
    {
        var (message, recipient) = SentToOne();
        message.ApplyDeliveryFeedback(recipient.Id, Outcome(DeliveryOutcome.Delivered, T0.AddMinutes(1)), "{}");

        var result = message.ApplyDeliveryFeedback(
            recipient.Id,
            Outcome(DeliveryOutcome.Deferred, T0.AddMinutes(2)),
            "{}"
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(DeliveryOutcome.Delivered, recipient.Outcome);
        Assert.Equal(T0.AddMinutes(1), recipient.OutcomeAtUtc);
        Assert.Contains(message.Events, e => e.EventType == SentMessageEventType.Deferred);
    }

    [Fact]
    public void ApplyDeliveryFeedback_complaint_after_delivered_wins()
    {
        var (message, recipient) = SentToOne();
        message.ApplyDeliveryFeedback(recipient.Id, Outcome(DeliveryOutcome.Delivered, T0.AddMinutes(1)), null);

        message.ApplyDeliveryFeedback(recipient.Id, Outcome(DeliveryOutcome.Complained, T0.AddHours(3)), null);

        Assert.Equal(DeliveryOutcome.Complained, recipient.Outcome);
        Assert.Equal(DeliveryOutcome.Complained, message.DeliverySummary);
    }

    [Fact]
    public void ApplyDeliveryFeedback_hard_bounce_is_final_and_keeps_the_reason()
    {
        var (message, recipient) = SentToOne();
        message.ApplyDeliveryFeedback(
            recipient.Id,
            Outcome(DeliveryOutcome.HardBounced, T0.AddMinutes(1), "550 5.1.1 user unknown"),
            null
        );

        message.ApplyDeliveryFeedback(recipient.Id, Outcome(DeliveryOutcome.Delivered, T0.AddMinutes(2)), null);

        Assert.Equal(DeliveryOutcome.HardBounced, recipient.Outcome);
        Assert.Equal("550 5.1.1 user unknown", recipient.ErrorReason);
        Assert.Contains(message.Events, e => e.EventType == SentMessageEventType.Bounced);
    }

    [Fact]
    public void ApplyDeliveryFeedback_counts_opens_and_clicks_without_touching_the_outcome()
    {
        var (message, recipient) = SentToOne();

        message.ApplyDeliveryFeedback(recipient.Id, Signal(EngagementSignal.Opened, T0.AddMinutes(5)), null);
        message.ApplyDeliveryFeedback(recipient.Id, Signal(EngagementSignal.Opened, T0.AddMinutes(2)), null);
        message.ApplyDeliveryFeedback(recipient.Id, Signal(EngagementSignal.Clicked, T0.AddMinutes(6)), null);

        Assert.Equal(2, recipient.OpenCount);
        Assert.Equal(T0.AddMinutes(2), recipient.FirstOpenedAtUtc);
        Assert.Equal(T0.AddMinutes(5), recipient.LastOpenedAtUtc);
        Assert.Equal(1, recipient.ClickCount);
        Assert.Equal(T0.AddMinutes(6), recipient.FirstClickedAtUtc);
        Assert.Equal(DeliveryOutcome.Accepted, recipient.Outcome);
    }

    [Fact]
    public void ApplyDeliveryFeedback_unsubscribe_keeps_the_first_time()
    {
        var (message, recipient) = SentToOne();

        message.ApplyDeliveryFeedback(recipient.Id, Signal(EngagementSignal.Unsubscribed, T0.AddMinutes(9)), null);
        message.ApplyDeliveryFeedback(recipient.Id, Signal(EngagementSignal.Unsubscribed, T0.AddMinutes(3)), null);

        Assert.Equal(T0.AddMinutes(3), recipient.UnsubscribedAtUtc);
    }

    [Fact]
    public void ApplyDeliveryFeedback_rejects_Accepted_because_only_our_send_sets_it()
    {
        var (message, recipient) = SentToOne();

        var result = message.ApplyDeliveryFeedback(recipient.Id, Outcome(DeliveryOutcome.Accepted, T0), null);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ApplyDeliveryFeedback_for_unknown_recipient_fails()
    {
        var (message, _) = SentToOne();

        var result = message.ApplyDeliveryFeedback(Guid.NewGuid(), Outcome(DeliveryOutcome.Delivered, T0), null);

        Assert.Equal("SentMessage.RecipientNotFound", result.Error.Code);
    }

    [Fact]
    public void DeliverySummary_shows_the_worst_recipient()
    {
        var message = CreateValidMessage();
        var delivered = message.AddRecipient("a@example.com", RecipientType.To, null).Value;
        var bounced = message.AddRecipient("b@example.com", RecipientType.Cc, null).Value;
        message.MarkAsSending();
        message.MarkAsSent("msg-id", T0);

        message.ApplyDeliveryFeedback(delivered.Id, Outcome(DeliveryOutcome.Delivered, T0.AddMinutes(1)), null);
        message.ApplyDeliveryFeedback(bounced.Id, Outcome(DeliveryOutcome.SoftBounced, T0.AddMinutes(1)), null);

        Assert.Equal(DeliveryOutcome.SoftBounced, message.DeliverySummary);
    }

    [Fact]
    public void FindRecipientByAddress_ignores_case_and_spaces()
    {
        var (message, recipient) = SentToOne();

        Assert.Same(recipient, message.FindRecipientByAddress("  Customer@Example.com "));
        Assert.Null(message.FindRecipientByAddress("someone@else.com"));
    }

    [Fact]
    public void Queue_records_the_delivery_lane()
    {
        var message = SentMessage
            .Queue(
                tenantId: Guid.NewGuid(),
                idempotencyKey: "key-lane",
                subject: "Hello",
                fromAddress: "office@example.com",
                stream: EmailStream.Transactional,
                providerCode: "gmail",
                notificationLogId: null,
                correlationId: null,
                fromDisplayName: null,
                replyTo: null,
                templateKey: null,
                queuedAtUtc: T0,
                deliveryLane: DeliveryLane.TenantMailbox
            )
            .Value;

        Assert.Equal(DeliveryLane.TenantMailbox, message.DeliveryLane);
        Assert.Equal(DeliveryLane.SystemProvider, CreateValidMessage().DeliveryLane);
    }

    [Fact]
    public void FallBackToSystemProvider_only_from_a_mailbox_send_in_progress()
    {
        // Por SMTP del sistema ya no hay otra vía a la que caer.
        var systemMessage = CreateValidMessage();
        systemMessage.AddRecipient("a@example.com", RecipientType.To, null);
        systemMessage.MarkAsSending();

        var result = systemMessage.FallBackToSystemProvider(
            "down",
            "system-smtp",
            "no-reply@taxoffix.com",
            null,
            "office@gmail.com",
            T0
        );

        Assert.Equal("SentMessage.FallbackNotAllowed", result.Error.Code);
        Assert.Empty(systemMessage.DeliveryAttempts);
    }

    [Fact]
    public void FallBackToSystemProvider_is_not_allowed_before_trying_the_mailbox()
    {
        var message = SentMessage
            .Queue(
                Guid.NewGuid(),
                "key-fb",
                "Hello",
                "office@gmail.com",
                EmailStream.Transactional,
                "gmail",
                null,
                null,
                null,
                null,
                null,
                T0,
                deliveryLane: DeliveryLane.TenantMailbox
            )
            .Value;

        var result = message.FallBackToSystemProvider(
            "down",
            "system-smtp",
            "no-reply@taxoffix.com",
            null,
            "office@gmail.com",
            T0
        );

        Assert.True(result.IsFailure);
        Assert.Equal(DeliveryLane.TenantMailbox, message.DeliveryLane);
    }
}
