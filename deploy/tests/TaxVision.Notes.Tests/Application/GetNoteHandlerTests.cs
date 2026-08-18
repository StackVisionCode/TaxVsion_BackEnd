using TaxVision.Notes.Application.Notes.Queries;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;

namespace TaxVision.Notes.Tests.Application;

public sealed class GetNoteHandlerTests
{
    private static Note MakeNote(Guid tenantId, Guid authorId, NoteVisibility visibility) =>
        Note.Create(
            tenantId,
            authorId,
            NoteContent.Create("<p>x</p>").Value,
            NoteReference.Create(NoteTargetType.None, null).Value,
            visibility,
            null
        ).Value;

    [Fact]
    public async Task Get_returns_NotFound_for_a_non_visible_private_note_never_revealing_it_exists()
    {
        var tenantId = Guid.NewGuid();
        var note = MakeNote(tenantId, Guid.NewGuid(), NoteVisibility.Private);
        var repo = new FakeNoteRepository();
        repo.Seed(note);

        var result = await GetNoteHandler.Handle(
            new GetNoteQuery(tenantId, note.Id, Guid.NewGuid(), ActorHasViewAll: false),
            repo,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task Get_returns_the_note_when_visible_to_the_actor()
    {
        var tenantId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var note = MakeNote(tenantId, authorId, NoteVisibility.Team);
        var repo = new FakeNoteRepository();
        repo.Seed(note);

        var result = await GetNoteHandler.Handle(
            new GetNoteQuery(tenantId, note.Id, Guid.NewGuid(), ActorHasViewAll: false),
            repo,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(note.Id, result.Value.Id);
    }
}
