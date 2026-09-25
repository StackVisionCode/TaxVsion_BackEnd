using TaxVision.Notes.Application.Notes.Commands;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;

namespace TaxVision.Notes.Tests.Application;

public sealed class UpdateNoteContentHandlerTests
{
    private static Note MakeNote(Guid tenantId, Guid authorId) =>
        Note.Create(
            tenantId,
            authorId,
            NoteContent.Create("<p>original</p>").Value,
            NoteReference.Create(NoteTargetType.None, null).Value,
            NoteVisibility.Team,
            null
        ).Value;

    [Fact]
    public async Task Update_succeeds_for_the_author()
    {
        var tenantId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var note = MakeNote(tenantId, authorId);
        var repo = new FakeNoteRepository();
        repo.Seed(note);
        var uow = new NoOpUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await UpdateNoteContentHandler.Handle(
            new UpdateNoteContentCommand(tenantId, note.Id, authorId, "<p>updated</p>", ActorHasViewAll: false),
            repo,
            new PassThroughHtmlSanitizer(),
            new FakeOffboardedStaffRepository(),
            uow,
            bus,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("<p>updated</p>", result.Value.ContentHtml);
        Assert.Equal(1, uow.SaveCount);
        Assert.Single(bus.Published);
    }

    [Fact]
    public async Task Update_is_forbidden_for_a_non_author_with_view_all_when_the_author_is_not_offboarded()
    {
        var tenantId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var note = MakeNote(tenantId, authorId);
        var repo = new FakeNoteRepository();
        repo.Seed(note);

        // view_all por sí solo NO habilita editar contenido ajeno — solo si el autor fue retirado.
        var result = await UpdateNoteContentHandler.Handle(
            new UpdateNoteContentCommand(tenantId, note.Id, Guid.NewGuid(), "<p>hijacked</p>", ActorHasViewAll: true),
            repo,
            new PassThroughHtmlSanitizer(),
            new FakeOffboardedStaffRepository(),
            new NoOpUnitOfWork(),
            new FakeMessageBus(),
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.Forbidden.Code, result.Error.Code);
    }

    [Fact]
    public async Task Update_succeeds_for_a_view_all_staff_when_the_author_was_offboarded()
    {
        var tenantId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var note = MakeNote(tenantId, authorId);
        var repo = new FakeNoteRepository();
        repo.Seed(note);
        var offboarded = new FakeOffboardedStaffRepository();
        offboarded.MarkOffboarded(tenantId, authorId);

        var result = await UpdateNoteContentHandler.Handle(
            new UpdateNoteContentCommand(tenantId, note.Id, Guid.NewGuid(), "<p>fixed</p>", ActorHasViewAll: true),
            repo,
            new PassThroughHtmlSanitizer(),
            offboarded,
            new NoOpUnitOfWork(),
            new FakeMessageBus(),
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("<p>fixed</p>", result.Value.ContentHtml);
    }

    [Fact]
    public async Task Update_fails_with_NotFound_when_note_does_not_exist_for_tenant()
    {
        var repo = new FakeNoteRepository();

        var result = await UpdateNoteContentHandler.Handle(
            new UpdateNoteContentCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "<p>x</p>",
                ActorHasViewAll: false
            ),
            repo,
            new PassThroughHtmlSanitizer(),
            new FakeOffboardedStaffRepository(),
            new NoOpUnitOfWork(),
            new FakeMessageBus(),
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.NotFound.Code, result.Error.Code);
    }
}
