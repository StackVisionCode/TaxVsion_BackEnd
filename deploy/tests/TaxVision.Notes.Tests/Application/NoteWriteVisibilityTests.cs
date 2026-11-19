using TaxVision.Notes.Application.Notes.Commands;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;

namespace TaxVision.Notes.Tests.Application;

// N3: escribir sobre una nota que el actor no puede VER responde 404; si la ve pero no puede tocarla, 403.
public sealed class NoteWriteVisibilityTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static (Note Note, FakeNoteRepository Repo) Seed(NoteVisibility visibility)
    {
        var note = Note.Create(
            TenantId,
            Guid.NewGuid(),
            NoteContent.Create("<p>ajena</p>").Value,
            NoteReference.Create(NoteTargetType.None, null).Value,
            visibility,
            null,
            DateTime.UtcNow
        ).Value;
        var repo = new FakeNoteRepository();
        repo.Seed(note);
        return (note, repo);
    }

    [Fact]
    public async Task Updating_another_users_private_note_returns_NotFound()
    {
        var (note, repo) = Seed(NoteVisibility.Private);

        var result = await UpdateNoteContentHandler.Handle(
            new UpdateNoteContentCommand(TenantId, note.Id, Guid.NewGuid(), "<p>x</p>", ActorHasViewAll: false),
            TestNoteReaders.For(repo),
            new PassThroughHtmlSanitizer(),
            new FakeOffboardedStaffRepository(),
            new NoOpUnitOfWork(),
            new FakeMessageBus(),
            new NoOpCorrelationContext(),
            TimeProvider.System,
            CancellationToken.None
        );

        Assert.Equal(NoteErrors.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task Pinning_another_users_private_note_returns_NotFound()
    {
        var (note, repo) = Seed(NoteVisibility.Private);

        var result = await PinNoteHandler.Handle(
            new PinNoteCommand(TenantId, note.Id, Guid.NewGuid(), ActorHasViewAll: false),
            TestNoteReaders.For(repo),
            new FakeOffboardedStaffRepository(),
            new NoOpUnitOfWork(),
            new FakeMessageBus(),
            new NoOpCorrelationContext(),
            TimeProvider.System,
            CancellationToken.None
        );

        Assert.Equal(NoteErrors.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task Deleting_another_users_private_note_returns_NotFound()
    {
        var (note, repo) = Seed(NoteVisibility.Private);

        var result = await DeleteNoteHandler.Handle(
            new DeleteNoteCommand(TenantId, note.Id, Guid.NewGuid(), ActorHasViewAll: false),
            TestNoteReaders.For(repo),
            new NoOpUnitOfWork(),
            new FakeMessageBus(),
            new NoOpCorrelationContext(),
            TimeProvider.System,
            CancellationToken.None
        );

        Assert.Equal(NoteErrors.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task Pinning_a_visible_team_note_of_another_author_returns_Forbidden()
    {
        var (note, repo) = Seed(NoteVisibility.Team);

        var result = await PinNoteHandler.Handle(
            new PinNoteCommand(TenantId, note.Id, Guid.NewGuid(), ActorHasViewAll: false),
            TestNoteReaders.For(repo),
            new FakeOffboardedStaffRepository(),
            new NoOpUnitOfWork(),
            new FakeMessageBus(),
            new NoOpCorrelationContext(),
            TimeProvider.System,
            CancellationToken.None
        );

        Assert.Equal(NoteErrors.Forbidden.Code, result.Error.Code);
    }

    [Fact]
    public async Task Deleting_a_visible_team_note_of_another_author_returns_Forbidden()
    {
        var (note, repo) = Seed(NoteVisibility.Team);

        var result = await DeleteNoteHandler.Handle(
            new DeleteNoteCommand(TenantId, note.Id, Guid.NewGuid(), ActorHasViewAll: false),
            TestNoteReaders.For(repo),
            new NoOpUnitOfWork(),
            new FakeMessageBus(),
            new NoOpCorrelationContext(),
            TimeProvider.System,
            CancellationToken.None
        );

        Assert.Equal(NoteErrors.Forbidden.Code, result.Error.Code);
    }

    [Theory]
    [InlineData(false, "Note.NotFound")]
    [InlineData(true, "Note.Forbidden")]
    public async Task Pinning_a_customer_note_respects_the_assignment_gate(bool assigned, string expectedCode)
    {
        var customerId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var note = Note.Create(
            TenantId,
            Guid.NewGuid(),
            NoteContent.Create("<p>cliente</p>").Value,
            NoteReference.Create(NoteTargetType.Customer, customerId).Value,
            NoteVisibility.Team,
            null,
            DateTime.UtcNow
        ).Value;
        var repo = new FakeNoteRepository();
        repo.Seed(note);
        if (assigned)
            repo.Assignments.Add((customerId, actorId));

        var result = await PinNoteHandler.Handle(
            new PinNoteCommand(TenantId, note.Id, actorId, ActorHasViewAll: false, CanViewAllCustomers: false),
            TestNoteReaders.For(repo, assignmentEnabled: true),
            new FakeOffboardedStaffRepository(),
            new NoOpUnitOfWork(),
            new FakeMessageBus(),
            new NoOpCorrelationContext(),
            TimeProvider.System,
            CancellationToken.None
        );

        Assert.Equal(expectedCode, result.Error.Code);
    }
}
