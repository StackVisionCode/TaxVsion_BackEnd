using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Tests.Domain;

public sealed class DraftTests
{
    private static EmailAddress Address(string value) => EmailAddress.Create(value).Value;

    private static ReplyContext ValidReplyContext() =>
        ReplyContext
            .Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "<original@example.com>",
                ["<a@example.com>", "<b@example.com>"],
                "provider-msg-original"
            )
            .Value;

    private static DraftAttachmentRef Attachment(long sizeBytes = 1024) =>
        DraftAttachmentRef.Create(Guid.NewGuid(), "file.pdf", "application/pdf", sizeBytes).Value;

    [Fact]
    public void CreateNew_fails_when_customerId_is_empty()
    {
        var result = Draft.CreateNew(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.CustomerIdRequired", result.Error.Code);
    }

    [Fact]
    public void CreateNew_fails_when_accountId_is_empty()
    {
        var result = Draft.CreateNew(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.AccountIdRequired", result.Error.Code);
    }

    /// <summary>RBAC Fase 4 (RBAC_Hardening_Plan.md) — el creador es obligatorio desde que existe este campo (ver IHasOwner).</summary>
    [Fact]
    public void CreateNew_fails_when_createdByUserId_is_empty()
    {
        var result = Draft.CreateNew(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.CreatedByUserIdRequired", result.Error.Code);
    }

    [Fact]
    public void CreateNew_succeeds_with_empty_subject_and_body_and_no_reply_context()
    {
        var result = Draft.CreateNew(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.IsSuccess);
        var draft = result.Value;
        Assert.Equal(DraftStatus.Draft, draft.Status);
        Assert.Equal(string.Empty, draft.Subject);
        Assert.Equal(string.Empty, draft.HtmlBody);
        Assert.Null(draft.TextBody);
        Assert.Null(draft.ReplyContext);
        Assert.Null(draft.EmailThreadId);
        Assert.Empty(draft.Recipients);
        Assert.Empty(draft.Attachments);
    }

    [Fact]
    public void CreateReply_prefixes_the_subject_with_Re_when_it_does_not_already_have_it()
    {
        var replyContext = ValidReplyContext();

        var result = Draft.CreateReply(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            replyContext,
            "Tax question"
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Re: Tax question", result.Value.Subject);
        Assert.Same(replyContext, result.Value.ReplyContext);
        Assert.Equal(replyContext.EmailThreadId, result.Value.EmailThreadId);
    }

    [Theory]
    [InlineData("Re: Tax question", "Re: Tax question")]
    [InlineData("RE: Tax question", "RE: Tax question")]
    [InlineData("re: Tax question", "re: Tax question")]
    public void CreateReply_does_not_double_prefix_a_subject_that_already_starts_with_Re(
        string originalSubject,
        string expectedSubject
    )
    {
        var result = Draft.CreateReply(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ValidReplyContext(),
            originalSubject
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedSubject, result.Value.Subject);
    }

    [Fact]
    public void CreateReply_fails_when_originalSubject_is_blank()
    {
        var result = Draft.CreateReply(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ValidReplyContext(),
            "   "
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.OriginalSubjectRequired", result.Error.Code);
    }

    [Fact]
    public void AutoSave_updates_only_the_non_null_fields_and_bumps_timestamps()
    {
        var draft = Draft.CreateNew(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;

        var result = draft.AutoSave("New subject", "<p>Hello</p>", null, null);

        Assert.True(result.IsSuccess);
        Assert.Equal("New subject", draft.Subject);
        Assert.Equal("<p>Hello</p>", draft.HtmlBody);
        Assert.Null(draft.TextBody);
        Assert.NotNull(draft.LastAutoSavedAtUtc);
        Assert.Equal(draft.UpdatedAtUtc, draft.LastAutoSavedAtUtc);
    }

    [Fact]
    public void AutoSave_replaces_the_recipients_collection_when_provided()
    {
        var draft = Draft.CreateNew(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;

        draft.AutoSave(
            null,
            null,
            null,
            [new DraftRecipientData(Address("a@example.com"), EmailRecipientType.To, "A")]
        );
        draft.AutoSave(
            null,
            null,
            null,
            [
                new DraftRecipientData(Address("b@example.com"), EmailRecipientType.Cc, "B"),
                new DraftRecipientData(Address("c@example.com"), EmailRecipientType.Bcc, null),
            ]
        );

        Assert.Equal(2, draft.Recipients.Count);
        Assert.DoesNotContain(draft.Recipients, r => r.Address == "a@example.com");
    }

    [Fact]
    public void AutoSave_fails_when_subject_exceeds_the_max_length()
    {
        var draft = Draft.CreateNew(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;

        var result = draft.AutoSave(new string('x', Draft.SubjectMaxLength + 1), null, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.SubjectTooLong", result.Error.Code);
    }

    [Theory]
    [InlineData(DraftStatus.Sending)]
    [InlineData(DraftStatus.Sent)]
    [InlineData(DraftStatus.Failed)]
    [InlineData(DraftStatus.Discarded)]
    public void AutoSave_fails_when_status_is_not_Draft(DraftStatus status)
    {
        var draft = DraftInStatus(status);

        var result = draft.AutoSave("x", null, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void AttachFile_appends_the_attachment_when_status_is_Draft()
    {
        var draft = Draft.CreateNew(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        var attachment = Attachment();

        var result = draft.AttachFile(attachment);

        Assert.True(result.IsSuccess);
        Assert.Single(draft.Attachments);
    }

    [Fact]
    public void AttachFile_attaching_the_same_fileId_twice_is_a_no_op()
    {
        var draft = Draft.CreateNew(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        var fileId = Guid.NewGuid();
        var first = DraftAttachmentRef.Create(fileId, "a.pdf", "application/pdf", 100).Value;
        var duplicate = DraftAttachmentRef.Create(fileId, "a-renamed.pdf", "application/pdf", 999).Value;

        draft.AttachFile(first);
        var result = draft.AttachFile(duplicate);

        Assert.True(result.IsSuccess);
        var only = Assert.Single(draft.Attachments);
        Assert.Equal("a.pdf", only.Filename);
    }

    [Fact]
    public void AttachFile_fails_when_status_is_not_Draft()
    {
        var draft = DraftInStatus(DraftStatus.Sending);

        var result = draft.AttachFile(Attachment());

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void RemoveAttachment_removes_an_existing_attachment()
    {
        var draft = Draft.CreateNew(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        var attachment = Attachment();
        draft.AttachFile(attachment);

        var result = draft.RemoveAttachment(attachment.FileId);

        Assert.True(result.IsSuccess);
        Assert.Empty(draft.Attachments);
    }

    [Fact]
    public void RemoveAttachment_is_a_tolerant_no_op_when_the_file_is_not_attached()
    {
        var draft = Draft.CreateNew(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;

        var result = draft.RemoveAttachment(Guid.NewGuid());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void RemoveAttachment_fails_when_status_is_not_Draft()
    {
        var draft = DraftInStatus(DraftStatus.Failed);

        var result = draft.RemoveAttachment(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void Discard_transitions_from_Draft_to_Discarded()
    {
        var draft = Draft.CreateNew(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;

        var result = draft.Discard();

        Assert.True(result.IsSuccess);
        Assert.Equal(DraftStatus.Discarded, draft.Status);
    }

    [Theory]
    [InlineData(DraftStatus.Sending)]
    [InlineData(DraftStatus.Sent)]
    [InlineData(DraftStatus.Failed)]
    [InlineData(DraftStatus.Discarded)]
    public void Discard_fails_when_status_is_not_Draft(DraftStatus status)
    {
        var draft = DraftInStatus(status);

        var result = draft.Discard();

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.InvalidTransition", result.Error.Code);
        Assert.Equal(status, draft.Status);
    }

    [Fact]
    public void MarkSending_transitions_from_Draft_to_Sending()
    {
        var draft = Draft.CreateNew(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;

        var result = draft.MarkSending();

        Assert.True(result.IsSuccess);
        Assert.Equal(DraftStatus.Sending, draft.Status);
    }

    [Fact]
    public void MarkSending_fails_when_status_is_not_Draft()
    {
        var draft = DraftInStatus(DraftStatus.Sending);

        var result = draft.MarkSending();

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void MarkSent_transitions_from_Sending_to_Sent_and_records_the_sentMessageId()
    {
        var draft = DraftInStatus(DraftStatus.Sending);
        var sentMessageId = Guid.NewGuid();

        var result = draft.MarkSent(sentMessageId);

        Assert.True(result.IsSuccess);
        Assert.Equal(DraftStatus.Sent, draft.Status);
        Assert.Equal(sentMessageId, draft.SentMessageId);
    }

    [Fact]
    public void MarkSent_fails_when_status_is_not_Sending()
    {
        var draft = Draft.CreateNew(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;

        var result = draft.MarkSent(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void MarkFailed_transitions_from_Sending_to_Failed_and_records_the_reason()
    {
        var draft = DraftInStatus(DraftStatus.Sending);

        var result = draft.MarkFailed("provider timeout");

        Assert.True(result.IsSuccess);
        Assert.Equal(DraftStatus.Failed, draft.Status);
        Assert.Equal("provider timeout", draft.FailureReason);
    }

    [Fact]
    public void MarkFailed_fails_when_status_is_not_Sending()
    {
        var draft = Draft.CreateNew(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;

        var result = draft.MarkFailed("provider timeout");

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.InvalidTransition", result.Error.Code);
    }

    private static Draft DraftInStatus(DraftStatus status)
    {
        var draft = Draft.CreateNew(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;

        if (status == DraftStatus.Draft)
            return draft;

        if (status == DraftStatus.Discarded)
        {
            draft.Discard();
            return draft;
        }

        draft.MarkSending();
        if (status == DraftStatus.Sending)
            return draft;

        if (status == DraftStatus.Sent)
        {
            draft.MarkSent(Guid.NewGuid());
            return draft;
        }

        if (status == DraftStatus.Failed)
        {
            draft.MarkFailed("reason");
            return draft;
        }

        throw new InvalidOperationException($"Unsupported status for this helper: {status}.");
    }
}
