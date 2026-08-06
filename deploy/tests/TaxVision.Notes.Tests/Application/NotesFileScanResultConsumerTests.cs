using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notes.Application.Notes.Consumers;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;

namespace TaxVision.Notes.Tests.Application;

/// <summary>
/// Fase 7 (03_Plan_De_Fases.md §Fase 7) — Caso B: CloudStorage ya escaneó/movió el archivo antes de
/// publicar. Cubre las 3 transiciones de <c>NoteAttachment</c> (Available/Rejected×2) + el guard de
/// tenant mismatch (nunca confiar ciegamente en el evento) + el detach reactivo por FileDeleted.
/// </summary>
public sealed class NotesFileScanResultConsumerTests
{
    private static (Note note, Guid fileId) SeedNoteWithAttachment(FakeNoteRepository repo, Guid tenantId)
    {
        var note = Note.Create(
            tenantId,
            Guid.NewGuid(),
            NoteContent.Create("<p>hi</p>").Value,
            NoteReference.Create(NoteTargetType.None, null).Value,
            NoteVisibility.Private,
            null
        ).Value;
        var fileId = Guid.NewGuid();
        note.AttachFile(fileId, "receipt.pdf", "application/pdf", 1024);
        repo.Seed(note);
        return (note, fileId);
    }

    [Fact]
    public async Task FileAvailable_marks_matching_attachment_Available()
    {
        var tenantId = Guid.NewGuid();
        var repo = new FakeNoteRepository();
        var (note, fileId) = SeedNoteWithAttachment(repo, tenantId);
        var uow = new NoOpUnitOfWork();

        await NotesFileScanResultConsumer.Handle(
            new FileAvailableIntegrationEvent
            {
                TenantId = tenantId,
                FileId = fileId,
                ObjectKey = "key",
                ContentType = "application/pdf",
                SizeBytes = 1024,
                ChecksumSha256 = "abc",
                CreatedBy = Guid.NewGuid(),
            },
            repo,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<Note>.Instance,
            CancellationToken.None
        );

        Assert.Equal(NoteAttachmentStatus.Available, note.Attachments.Single().Status);
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task FileInfectedDetected_marks_matching_attachment_Rejected_with_infected_reason()
    {
        var tenantId = Guid.NewGuid();
        var repo = new FakeNoteRepository();
        var (note, fileId) = SeedNoteWithAttachment(repo, tenantId);
        var uow = new NoOpUnitOfWork();

        await NotesFileScanResultConsumer.Handle(
            new FileInfectedDetectedIntegrationEvent
            {
                TenantId = tenantId,
                FileId = fileId,
                ObjectKey = "key",
                ScanReport = "eicar-test",
            },
            repo,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<Note>.Instance,
            CancellationToken.None
        );

        var attachment = note.Attachments.Single();
        Assert.Equal(NoteAttachmentStatus.Rejected, attachment.Status);
        Assert.Equal("infected", attachment.RejectionReason);
    }

    [Fact]
    public async Task FileBlockedByPolicy_marks_matching_attachment_Rejected_with_blocked_reason()
    {
        var tenantId = Guid.NewGuid();
        var repo = new FakeNoteRepository();
        var (note, fileId) = SeedNoteWithAttachment(repo, tenantId);
        var uow = new NoOpUnitOfWork();

        await NotesFileScanResultConsumer.Handle(
            new FileBlockedByPolicyIntegrationEvent
            {
                TenantId = tenantId,
                FileId = fileId,
                ObjectKey = "key",
                PolicyReason = "nsfw",
                CreatedBy = Guid.NewGuid(),
            },
            repo,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<Note>.Instance,
            CancellationToken.None
        );

        var attachment = note.Attachments.Single();
        Assert.Equal(NoteAttachmentStatus.Rejected, attachment.Status);
        Assert.Equal("blocked-by-policy", attachment.RejectionReason);
    }

    [Fact]
    public async Task FileDeleted_detaches_matching_attachment_and_publishes_detached_event()
    {
        var tenantId = Guid.NewGuid();
        var repo = new FakeNoteRepository();
        var (note, fileId) = SeedNoteWithAttachment(repo, tenantId);
        var uow = new NoOpUnitOfWork();
        var bus = new FakeMessageBus();

        await NotesFileScanResultConsumer.Handle(
            new FileDeletedIntegrationEvent
            {
                TenantId = tenantId,
                FileId = fileId,
                CreatedBy = Guid.NewGuid(),
            },
            repo,
            uow,
            bus,
            new NoOpCorrelationContext(),
            NullLogger<Note>.Instance,
            CancellationToken.None
        );

        Assert.Equal(NoteAttachmentStatus.Detached, note.Attachments.Single().Status);
        Assert.Single(bus.Published);
    }

    [Fact]
    public async Task Tenant_mismatch_is_ignored_and_never_mutates_the_note()
    {
        var repo = new FakeNoteRepository();
        var (note, fileId) = SeedNoteWithAttachment(repo, Guid.NewGuid());
        var uow = new NoOpUnitOfWork();
        var otherTenantId = Guid.NewGuid(); // deliberadamente distinto del dueño real de la nota

        await NotesFileScanResultConsumer.Handle(
            new FileAvailableIntegrationEvent
            {
                TenantId = otherTenantId,
                FileId = fileId,
                ObjectKey = "key",
                ContentType = "application/pdf",
                SizeBytes = 1024,
                ChecksumSha256 = "abc",
                CreatedBy = Guid.NewGuid(),
            },
            repo,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<Note>.Instance,
            CancellationToken.None
        );

        Assert.Equal(NoteAttachmentStatus.Pending, note.Attachments.Single().Status);
        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public async Task Unknown_fileId_is_a_silent_noop()
    {
        var repo = new FakeNoteRepository();
        var uow = new NoOpUnitOfWork();

        await NotesFileScanResultConsumer.Handle(
            new FileAvailableIntegrationEvent
            {
                TenantId = Guid.NewGuid(),
                FileId = Guid.NewGuid(), // no corresponde a ningún adjunto de Notes
                ObjectKey = "key",
                ContentType = "application/pdf",
                SizeBytes = 1024,
                ChecksumSha256 = "abc",
                CreatedBy = Guid.NewGuid(),
            },
            repo,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<Note>.Instance,
            CancellationToken.None
        );

        Assert.Equal(0, uow.SaveCount);
    }
}
