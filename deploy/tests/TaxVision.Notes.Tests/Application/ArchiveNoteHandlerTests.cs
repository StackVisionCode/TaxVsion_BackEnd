using TaxVision.Notes.Application.Notes.Commands;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;

namespace TaxVision.Notes.Tests.Application;

public sealed class ArchiveNoteHandlerTests
{
    private static Note MakeNote(Guid tenantId, Guid authorId) =>
        Note.Create(
            tenantId,
            authorId,
            NoteContent.Create("<p>x</p>").Value,
            NoteReference.Create(NoteTargetType.None, null).Value,
            NoteVisibility.Private,
            null
        ).Value;

    [Fact]
    public async Task Archive_succeeds_for_the_author()
    {
        var tenantId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var note = MakeNote(tenantId, authorId);
        var repo = new FakeNoteRepository();
        repo.Seed(note);
        var uow = new NoOpUnitOfWork();

        var result = await ArchiveNoteHandler.Handle(
            new ArchiveNoteCommand(tenantId, note.Id, authorId, ActorHasViewAll: false),
            repo,
            uow,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(NoteStatus.Archived.ToString(), result.Value.Status);
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task Archive_succeeds_for_a_non_author_with_view_all_governance_permission()
    {
        var tenantId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var note = MakeNote(tenantId, authorId);
        var repo = new FakeNoteRepository();
        repo.Seed(note);

        var result = await ArchiveNoteHandler.Handle(
            new ArchiveNoteCommand(tenantId, note.Id, Guid.NewGuid(), ActorHasViewAll: true),
            repo,
            new NoOpUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Archive_is_forbidden_for_a_non_author_without_view_all()
    {
        var tenantId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var note = MakeNote(tenantId, authorId);
        var repo = new FakeNoteRepository();
        repo.Seed(note);

        var result = await ArchiveNoteHandler.Handle(
            new ArchiveNoteCommand(tenantId, note.Id, Guid.NewGuid(), ActorHasViewAll: false),
            repo,
            new NoOpUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.Forbidden.Code, result.Error.Code);
    }
}
