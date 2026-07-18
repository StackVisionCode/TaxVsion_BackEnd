using TaxVision.Postmaster.Domain.Sending;

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
    public void RecordDeliveryEvent_applies_suppressed_to_recipient_without_bubbling_message_status()
    {
        var message = CreateValidMessage();
        var recipient = message.AddRecipient("customer@example.com", RecipientType.To, null).Value;
        message.MarkAsSending();
        message.MarkAsSent(null, DateTime.UtcNow);

        var result = message.RecordDeliveryEvent(
            recipient.Id,
            SentMessageEventType.Suppressed,
            DateTime.UtcNow,
            rawPayload: null,
            reason: "Address in suppression list."
        );

        Assert.True(result.IsSuccess);
        // RecordDeliveryEvent no hace bubble-up del Status del mensaje (eso lo maneja el caller
        // explícitamente vía MarkAsSuppressed cuando corresponde) — solo el recipient cambia.
        Assert.Equal(SentMessageStatus.Sent, message.Status);
        Assert.Equal(RecipientStatus.Suppressed, recipient.Status);
    }

    [Fact]
    public void RecordDeliveryEvent_for_unknown_recipient_fails()
    {
        var message = CreateValidMessage();
        message.MarkAsSending();
        message.MarkAsSent(null, DateTime.UtcNow);

        var result = message.RecordDeliveryEvent(
            Guid.NewGuid(),
            SentMessageEventType.Suppressed,
            DateTime.UtcNow,
            null,
            null
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SentMessage.RecipientNotFound", result.Error.Code);
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
}
