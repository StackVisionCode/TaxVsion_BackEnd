using TaxVision.Notes.Application.Notes;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;

namespace TaxVision.Notes.Tests.Application;

/// <summary>Tests de la regla pura de visibilidad/autoría (03_Plan_De_Fases.md §Fase 5) — sin fakes, solo el aggregate real.</summary>
public sealed class NoteVisibilityPolicyTests
{
    private static Note MakeNote(NoteVisibility visibility, Guid authorId, NoteStatus status = NoteStatus.Active)
    {
        var content = NoteContent.Create("hello").Value;
        var reference = NoteReference.Create(NoteTargetType.None, null).Value;
        var note = Note.Create(Guid.NewGuid(), authorId, content, reference, visibility, null).Value;
        if (status == NoteStatus.Archived)
            note.Archive(authorId);
        else if (status == NoteStatus.Deleted)
            note.SoftDelete(authorId);
        return note;
    }

    [Theory]
    [InlineData(NoteVisibility.ClientVisible)]
    [InlineData(NoteVisibility.Team)]
    public void CanStaffView_returns_true_for_ClientVisible_and_Team_regardless_of_author(NoteVisibility visibility)
    {
        var note = MakeNote(visibility, Guid.NewGuid());
        Assert.True(NoteVisibilityPolicy.CanStaffView(note, Guid.NewGuid(), actorHasViewAll: false));
    }

    [Fact]
    public void CanStaffView_returns_true_for_own_Private_note()
    {
        var authorId = Guid.NewGuid();
        var note = MakeNote(NoteVisibility.Private, authorId);
        Assert.True(NoteVisibilityPolicy.CanStaffView(note, authorId, actorHasViewAll: false));
    }

    [Fact]
    public void CanStaffView_returns_false_for_others_Private_note_without_view_all()
    {
        var note = MakeNote(NoteVisibility.Private, Guid.NewGuid());
        Assert.False(NoteVisibilityPolicy.CanStaffView(note, Guid.NewGuid(), actorHasViewAll: false));
    }

    [Fact]
    public void CanStaffView_returns_true_for_others_Private_note_with_view_all()
    {
        var note = MakeNote(NoteVisibility.Private, Guid.NewGuid());
        Assert.True(NoteVisibilityPolicy.CanStaffView(note, Guid.NewGuid(), actorHasViewAll: true));
    }

    [Fact]
    public void CanStaffView_returns_false_for_deleted_note_without_view_all()
    {
        var authorId = Guid.NewGuid();
        var note = MakeNote(NoteVisibility.ClientVisible, authorId, NoteStatus.Deleted);
        Assert.False(NoteVisibilityPolicy.CanStaffView(note, authorId, actorHasViewAll: false));
    }

    [Fact]
    public void CanStaffView_returns_true_for_deleted_note_with_view_all()
    {
        var note = MakeNote(NoteVisibility.ClientVisible, Guid.NewGuid(), NoteStatus.Deleted);
        Assert.True(NoteVisibilityPolicy.CanStaffView(note, Guid.NewGuid(), actorHasViewAll: true));
    }

    [Fact]
    public void CanEditContent_is_true_only_for_the_author_even_with_view_all()
    {
        var authorId = Guid.NewGuid();
        var note = MakeNote(NoteVisibility.Team, authorId);

        Assert.True(NoteVisibilityPolicy.CanEditContent(note, authorId));
        Assert.False(NoteVisibilityPolicy.CanEditContent(note, Guid.NewGuid()));
    }

    [Fact]
    public void CanManage_allows_author_or_view_all_but_not_a_third_party()
    {
        var authorId = Guid.NewGuid();
        var note = MakeNote(NoteVisibility.Team, authorId);
        var stranger = Guid.NewGuid();

        Assert.True(NoteVisibilityPolicy.CanManage(note, authorId, actorHasViewAll: false));
        Assert.True(NoteVisibilityPolicy.CanManage(note, stranger, actorHasViewAll: true));
        Assert.False(NoteVisibilityPolicy.CanManage(note, stranger, actorHasViewAll: false));
    }
}
