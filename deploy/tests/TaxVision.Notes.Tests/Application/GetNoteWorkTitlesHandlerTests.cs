using BuildingBlocks.Contracts.Work;
using Microsoft.Extensions.Options;
using TaxVision.Notes.Application.Notes;
using TaxVision.Notes.Application.Notes.Queries;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;

namespace TaxVision.Notes.Tests.Application;

/// <summary>Títulos en vivo de notas: el texto visible se resume en una línea y la asignación sigue al feed.</summary>
public sealed class GetNoteWorkTitlesHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    [Fact]
    public async Task Title_is_the_start_of_the_visible_text_in_one_clipped_line()
    {
        var repo = new FakeNoteRepository();
        var shortNote = Seed(repo, "<p>Llamar a Ana</p><p>por el W-2</p>");
        var longNote = Seed(repo, $"<p>{new string('x', 300)}</p>");

        var response = await Handle(repo, [shortNote.Id, longNote.Id]);

        Assert.Equal(
            new WorkTitle(WorkResourceTypes.Note, shortNote.Id, "Llamar a Ana por el W-2"),
            response.Items.Single(item => item.Id == shortNote.Id)
        );
        Assert.Equal(WorkLimits.TitleMaxLength, response.Items.Single(item => item.Id == longNote.Id).Title.Length);
    }

    [Fact]
    public async Task Private_notes_of_others_are_omitted_without_view_all()
    {
        var repo = new FakeNoteRepository();
        var othersPrivate = Seed(repo, "<p>secreto</p>", Guid.NewGuid(), NoteVisibility.Private);
        var team = Seed(repo, "<p>equipo</p>", Guid.NewGuid(), NoteVisibility.Team);

        var response = await Handle(repo, [othersPrivate.Id, team.Id]);

        Assert.Equal([team.Id], response.Items.Select(item => item.Id));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task Assignment_filter_applies_only_with_the_flag_and_without_customers_view_all(
        bool flagEnabled,
        bool canViewAllCustomers,
        bool expectRestricted
    )
    {
        var repo = new FakeNoteRepository();
        var note = Seed(repo, "<p>nota</p>");

        await Handle(repo, [note.Id], flagEnabled, canViewAllCustomers);

        Assert.Equal(expectRestricted ? ActorId : null, repo.LastTitles!.Value.AssignedToUserId);
    }

    // ----- helpers -----

    private static Task<WorkTitlesResponse> Handle(
        FakeNoteRepository repo,
        IReadOnlyList<Guid> ids,
        bool flagEnabled = false,
        bool canViewAllCustomers = true
    ) =>
        GetNoteWorkTitlesHandler.Handle(
            new GetNoteWorkTitlesQuery(TenantId, ActorId, false, canViewAllCustomers, ids),
            repo,
            Options.Create(new NotesVisibilityOptions { Enabled = flagEnabled }),
            CancellationToken.None
        );

    private static Note Seed(
        FakeNoteRepository repo,
        string html,
        Guid? author = null,
        NoteVisibility visibility = NoteVisibility.Team
    )
    {
        var note = Note.Create(
            TenantId,
            author ?? ActorId,
            NoteContent.Create(html).Value,
            NoteReference.Create(NoteTargetType.None, null).Value,
            visibility,
            null,
            DateTime.UtcNow
        ).Value;
        repo.Seed(note);
        return note;
    }
}
