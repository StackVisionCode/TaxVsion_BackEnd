using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;

namespace TaxVision.Notes.Tests.Domain;

public class NoteTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AuthorId = Guid.NewGuid();

    private static NoteContent ValidContent(string text = "Hello") => NoteContent.Create($"<p>{text}</p>").Value;

    private static NoteReference NoReference() => NoteReference.Create(NoteTargetType.None, null).Value;

    private static Note CreateValidNote() =>
        Note.Create(
            TenantId,
            AuthorId,
            ValidContent(),
            NoReference(),
            NoteVisibility.Private,
            null,
            DateTime.UtcNow
        ).Value;

    [Fact]
    public void AttachFile_UsesProvidedTimestamp()
    {
        var note = CreateValidNote();
        var nowUtc = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

        var result = note.AttachFile(Guid.NewGuid(), "receipt.pdf", "application/pdf", 1024, nowUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(nowUtc, note.UpdatedAtUtc);
        Assert.Equal(nowUtc, Assert.Single(note.Attachments).LinkedAtUtc);
    }

    [Fact]
    public void Create_UsesProvidedTimestamp()
    {
        var nowUtc = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

        var note = Note.Create(
            TenantId,
            AuthorId,
            ValidContent(),
            NoReference(),
            NoteVisibility.Private,
            null,
            nowUtc
        ).Value;

        Assert.Equal(nowUtc, note.CreatedAtUtc);
        Assert.Equal(nowUtc, note.UpdatedAtUtc);
    }

    [Fact]
    public void Create_WithValidInputs_Succeeds()
    {
        var result = Note.Create(
            TenantId,
            AuthorId,
            ValidContent(),
            NoReference(),
            NoteVisibility.Private,
            null,
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        var note = result.Value;
        Assert.Equal(TenantId, note.TenantId);
        Assert.Equal(AuthorId, note.CreatedByUserId);
        Assert.Equal(NoteStatus.Active, note.Status);
        Assert.False(note.IsPinned);
        Assert.Empty(note.Attachments);
    }

    [Fact]
    public void Create_WithEmptyTenantId_Fails()
    {
        var result = Note.Create(
            Guid.Empty,
            AuthorId,
            ValidContent(),
            NoReference(),
            NoteVisibility.Private,
            null,
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_WithEmptyAuthorId_Fails()
    {
        var result = Note.Create(
            TenantId,
            Guid.Empty,
            ValidContent(),
            NoReference(),
            NoteVisibility.Private,
            null,
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void SoftDelete_ThenUpdateContent_Fails()
    {
        var note = CreateValidNote();
        note.SoftDelete(AuthorId, DateTime.UtcNow);

        var result = note.UpdateContent(ValidContent("Edited"), AuthorId, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.Deleted, result.Error);
    }

    [Fact]
    public void SoftDelete_ThenChangeVisibility_Fails()
    {
        var note = CreateValidNote();
        note.SoftDelete(AuthorId, DateTime.UtcNow);

        var result = note.ChangeVisibility(NoteVisibility.Team, AuthorId, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.Deleted, result.Error);
    }

    [Fact]
    public void SoftDelete_Twice_FailsSecondTime()
    {
        var note = CreateValidNote();
        note.SoftDelete(AuthorId, DateTime.UtcNow);

        var result = note.SoftDelete(AuthorId, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.Deleted, result.Error);
    }

    [Fact]
    public void Archive_ThenArchiveAgain_Fails()
    {
        var note = CreateValidNote();
        note.Archive(AuthorId, DateTime.UtcNow);

        var result = note.Archive(AuthorId, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.InvalidTransition, result.Error);
    }

    [Fact]
    public void Restore_WhenAlreadyActive_Fails()
    {
        var note = CreateValidNote();

        var result = note.Restore(AuthorId, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.InvalidTransition, result.Error);
    }

    [Fact]
    public void Archive_ThenRestore_ReturnsToActive()
    {
        var note = CreateValidNote();
        note.Archive(AuthorId, DateTime.UtcNow);

        var result = note.Restore(AuthorId, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(NoteStatus.Active, note.Status);
    }

    [Fact]
    public void Archive_WhenDeleted_Fails()
    {
        var note = CreateValidNote();
        note.SoftDelete(AuthorId, DateTime.UtcNow);

        var result = note.Archive(AuthorId, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.Deleted, result.Error);
    }

    [Fact]
    public void Pin_UpdatesIsPinnedAndUpdatedAtUtc()
    {
        var note = CreateValidNote();
        var before = note.UpdatedAtUtc;
        Thread.Sleep(5);

        var result = note.Pin(AuthorId, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.True(note.IsPinned);
        Assert.True(note.UpdatedAtUtc > before);
    }

    [Fact]
    public void Unpin_UpdatesIsPinnedAndUpdatedAtUtc()
    {
        var note = CreateValidNote();
        note.Pin(AuthorId, DateTime.UtcNow);
        var before = note.UpdatedAtUtc;
        Thread.Sleep(5);

        var result = note.Unpin(AuthorId, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.False(note.IsPinned);
        Assert.True(note.UpdatedAtUtc > before);
    }

    [Fact]
    public void ChangeVisibility_UpdatesVisibilityAndUpdatedAtUtc()
    {
        var note = CreateValidNote();
        var before = note.UpdatedAtUtc;
        Thread.Sleep(5);

        var result = note.ChangeVisibility(NoteVisibility.ClientVisible, AuthorId, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(NoteVisibility.ClientVisible, note.Visibility);
        Assert.True(note.UpdatedAtUtc > before);
    }

    [Fact]
    public void AttachFile_UpToLimit_Succeeds()
    {
        var note = CreateValidNote();

        for (var i = 0; i < Note.MaxAttachmentsPerNote; i++)
        {
            var result = note.AttachFile(Guid.NewGuid(), $"file{i}.pdf", "application/pdf", 1024, DateTime.UtcNow);
            Assert.True(result.IsSuccess);
        }

        Assert.Equal(Note.MaxAttachmentsPerNote, note.Attachments.Count);
    }

    [Fact]
    public void AttachFile_BeyondLimit_Fails()
    {
        var note = CreateValidNote();
        for (var i = 0; i < Note.MaxAttachmentsPerNote; i++)
            note.AttachFile(Guid.NewGuid(), $"file{i}.pdf", "application/pdf", 1024, DateTime.UtcNow);

        var result = note.AttachFile(Guid.NewGuid(), "one-too-many.pdf", "application/pdf", 1024, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.AttachmentLimit, result.Error);
    }

    [Fact]
    public void AttachFile_DuplicateFileId_Fails()
    {
        var note = CreateValidNote();
        var fileId = Guid.NewGuid();
        note.AttachFile(fileId, "a.pdf", "application/pdf", 1024, DateTime.UtcNow);

        var result = note.AttachFile(fileId, "a-again.pdf", "application/pdf", 1024, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.AttachmentDuplicate, result.Error);
    }

    [Fact]
    public void MarkAttachmentAvailable_ForUnknownFile_Fails()
    {
        var note = CreateValidNote();

        var result = note.MarkAttachmentAvailable(Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.AttachmentNotFound, result.Error);
    }

    [Fact]
    public void MarkAttachmentAvailable_CalledTwice_IsIdempotent()
    {
        var note = CreateValidNote();
        var fileId = Guid.NewGuid();
        note.AttachFile(fileId, "a.pdf", "application/pdf", 1024, DateTime.UtcNow);

        var first = note.MarkAttachmentAvailable(fileId, DateTime.UtcNow);
        var second = note.MarkAttachmentAvailable(fileId, DateTime.UtcNow);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(NoteAttachmentStatus.Available, note.Attachments.Single().Status);
    }

    [Fact]
    public void MarkAttachmentRejected_SetsStatusAndReason()
    {
        var note = CreateValidNote();
        var fileId = Guid.NewGuid();
        note.AttachFile(fileId, "a.pdf", "application/pdf", 1024, DateTime.UtcNow);

        var result = note.MarkAttachmentRejected(fileId, "infected", DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        var attachment = note.Attachments.Single();
        Assert.Equal(NoteAttachmentStatus.Rejected, attachment.Status);
        Assert.Equal("infected", attachment.RejectionReason);
    }

    [Fact]
    public void MarkAttachmentAvailable_AfterDetach_StaysDetached()
    {
        var note = CreateValidNote();
        var fileId = Guid.NewGuid();
        note.AttachFile(fileId, "a.pdf", "application/pdf", 1024, DateTime.UtcNow);
        note.DetachFile(fileId, DateTime.UtcNow);

        var result = note.MarkAttachmentAvailable(fileId, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(NoteAttachmentStatus.Detached, note.Attachments.Single().Status);
    }

    [Fact]
    public void MarkAttachmentRejected_AfterDetach_StaysDetached()
    {
        var note = CreateValidNote();
        var fileId = Guid.NewGuid();
        note.AttachFile(fileId, "a.pdf", "application/pdf", 1024, DateTime.UtcNow);
        note.DetachFile(fileId, DateTime.UtcNow);

        var result = note.MarkAttachmentRejected(fileId, "infected", DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        var attachment = note.Attachments.Single();
        Assert.Equal(NoteAttachmentStatus.Detached, attachment.Status);
        Assert.Null(attachment.RejectionReason);
    }

    [Fact]
    public void MarkAttachmentAvailable_AfterRejectedInfected_StaysRejected()
    {
        var note = CreateValidNote();
        var fileId = Guid.NewGuid();
        note.AttachFile(fileId, "a.pdf", "application/pdf", 1024, DateTime.UtcNow);
        note.MarkAttachmentRejected(fileId, "infected", DateTime.UtcNow);

        var result = note.MarkAttachmentAvailable(fileId, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        var attachment = note.Attachments.Single();
        Assert.Equal(NoteAttachmentStatus.Rejected, attachment.Status);
        Assert.Equal("infected", attachment.RejectionReason);
    }

    [Theory]
    [InlineData("scan-failed")]
    [InlineData("file-missing")]
    [InlineData("blocked-by-policy")]
    public void MarkAttachmentAvailable_AfterRejectedScanFailed_BecomesAvailable(string reason)
    {
        // D-NTS-2: un FileAvailable posterior al rechazo solo existe si CloudStorage reescaneó o reinstaló.
        var note = CreateValidNote();
        var fileId = Guid.NewGuid();
        note.AttachFile(fileId, "a.pdf", "application/pdf", 1024, DateTime.UtcNow);
        note.MarkAttachmentRejected(fileId, reason, DateTime.UtcNow);

        var result = note.MarkAttachmentAvailable(fileId, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        var attachment = note.Attachments.Single();
        Assert.Equal(NoteAttachmentStatus.Available, attachment.Status);
        Assert.Null(attachment.RejectionReason);
    }

    [Fact]
    public void MarkAttachmentAvailable_AfterDetach_DoesNotTouchTheNote()
    {
        var note = CreateValidNote();
        var fileId = Guid.NewGuid();
        note.AttachFile(fileId, "a.pdf", "application/pdf", 1024, DateTime.UtcNow);
        note.DetachFile(fileId, DateTime.UtcNow);
        var updatedAt = note.UpdatedAtUtc;

        note.MarkAttachmentAvailable(fileId, DateTime.UtcNow);

        Assert.Equal(updatedAt, note.UpdatedAtUtc);
    }

    [Fact]
    public void DetachFile_MovesToDetachedStatus()
    {
        var note = CreateValidNote();
        var fileId = Guid.NewGuid();
        note.AttachFile(fileId, "a.pdf", "application/pdf", 1024, DateTime.UtcNow);

        var result = note.DetachFile(fileId, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(NoteAttachmentStatus.Detached, note.Attachments.Single().Status);
    }

    [Fact]
    public void AttachFile_WhenDeleted_Fails()
    {
        var note = CreateValidNote();
        note.SoftDelete(AuthorId, DateTime.UtcNow);

        var result = note.AttachFile(Guid.NewGuid(), "a.pdf", "application/pdf", 1024, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.Deleted, result.Error);
    }
}
