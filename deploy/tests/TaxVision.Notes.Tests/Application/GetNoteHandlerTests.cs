using Microsoft.Extensions.Options;
using TaxVision.Notes.Application.Notes;
using TaxVision.Notes.Application.Notes.Queries;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;

namespace TaxVision.Notes.Tests.Application;

public sealed class GetNoteHandlerTests
{
    private static IOptions<NotesVisibilityOptions> Visibility(bool enabled = false) =>
        Options.Create(new NotesVisibilityOptions { Enabled = enabled });

    private static Note MakeNote(
        Guid tenantId,
        Guid authorId,
        NoteVisibility visibility,
        NoteTargetType targetType = NoteTargetType.None,
        Guid? targetId = null
    ) =>
        Note.Create(
            tenantId,
            authorId,
            NoteContent.Create("<p>x</p>").Value,
            NoteReference.Create(targetType, targetId).Value,
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
            Visibility(),
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
            Visibility(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(note.Id, result.Value.Id);
    }

    [Fact]
    public async Task Get_returns_NotFound_for_a_customer_note_when_actor_is_not_assigned_and_flag_on()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        // Nota Team (visible por CanStaffView), pero de un cliente NO asignado al actor.
        var note = MakeNote(tenantId, Guid.NewGuid(), NoteVisibility.Team, NoteTargetType.Customer, customerId);
        var repo = new FakeNoteRepository();
        repo.Seed(note);

        var result = await GetNoteHandler.Handle(
            new GetNoteQuery(tenantId, note.Id, actorId, ActorHasViewAll: false, CanViewAllCustomers: false),
            repo,
            Visibility(enabled: true),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task Get_returns_the_customer_note_when_actor_is_assigned_and_flag_on()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var note = MakeNote(tenantId, Guid.NewGuid(), NoteVisibility.Team, NoteTargetType.Customer, customerId);
        var repo = new FakeNoteRepository();
        repo.Seed(note);
        repo.Assignments.Add((customerId, actorId));

        var result = await GetNoteHandler.Handle(
            new GetNoteQuery(tenantId, note.Id, actorId, ActorHasViewAll: false, CanViewAllCustomers: false),
            repo,
            Visibility(enabled: true),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(note.Id, result.Value.Id);
    }

    [Fact]
    public async Task Get_returns_the_customer_note_of_an_unassigned_actor_when_can_view_all_customers()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var note = MakeNote(tenantId, Guid.NewGuid(), NoteVisibility.Team, NoteTargetType.Customer, customerId);
        var repo = new FakeNoteRepository();
        repo.Seed(note);

        // Admin (customers.view_all) ve la nota aunque no tenga fila de asignación.
        var result = await GetNoteHandler.Handle(
            new GetNoteQuery(tenantId, note.Id, Guid.NewGuid(), ActorHasViewAll: false, CanViewAllCustomers: true),
            repo,
            Visibility(enabled: true),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
    }
}
