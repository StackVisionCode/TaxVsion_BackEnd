using BuildingBlocks.Contracts.Files;
using Microsoft.Extensions.Options;
using TaxVision.Notes.Application.Notes;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Application.Notes.Queries;
using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Tests.Application;

/// <summary>
/// Archivos de notas que ve quien pregunta: la regla del feed llega al repositorio (privadas, asignación) y cada
/// archivo sale una vez, con la nota desde la que se ve y su marca de privacidad.
/// </summary>
public sealed class GetVisibleNoteFilesHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Colleague = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task The_feed_rule_reaches_the_repository(bool assignmentOn, bool canViewAllCustomers, bool restricted)
    {
        var repo = new FakeNoteRepository();
        var fileId = Guid.NewGuid();

        await HandleAsync(repo, [fileId], hasViewAll: true, canViewAllCustomers, assignmentOn);

        var call = Assert.Single(repo.VisibleFileCalls);
        Assert.Equal(
            (TenantId, Actor, true, restricted ? Actor : (Guid?)null),
            (call.TenantId, call.ActorUserId, call.ActorHasViewAll, call.AssignedToUserId)
        );
        Assert.Equal([fileId], call.FileIds);
    }

    [Fact]
    public async Task A_file_points_to_its_note_with_title_and_customer()
    {
        var fileId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var repo = new FakeNoteRepository();
        repo.VisibleFiles.Add(
            Row(
                fileId,
                noteId,
                NoteVisibility.Team,
                Colleague,
                NoteTargetType.Customer,
                customerId,
                Now,
                "\n Call Ana \nlater"
            )
        );

        var file = Assert.Single(await HandleAsync(repo, [fileId]));

        Assert.Equal(
            (fileId, "note", noteId, "Call Ana", (Guid?)customerId, (string?)null),
            (file.FileId, file.ResourceType, file.ResourceId, file.Title, file.CustomerId, file.RestrictedTo)
        );
    }

    [Fact]
    public async Task A_note_on_something_else_than_a_customer_has_no_customer()
    {
        var fileId = Guid.NewGuid();
        var repo = new FakeNoteRepository();
        repo.VisibleFiles.Add(
            Row(fileId, Guid.NewGuid(), NoteVisibility.Team, Colleague, NoteTargetType.Task, Guid.NewGuid(), Now)
        );

        Assert.Null(Assert.Single(await HandleAsync(repo, [fileId])).CustomerId);
    }

    [Fact]
    public async Task A_private_note_is_marked_only_you_for_its_author_and_admin_view_for_others()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var repo = new FakeNoteRepository();
        repo.VisibleFiles.Add(Row(mine, Guid.NewGuid(), NoteVisibility.Private, Actor, NoteTargetType.None, null, Now));
        repo.VisibleFiles.Add(
            Row(theirs, Guid.NewGuid(), NoteVisibility.Private, Colleague, NoteTargetType.None, null, Now)
        );

        var files = await HandleAsync(repo, [mine, theirs], hasViewAll: true);

        Assert.Equal(FileVisibilityRestrictions.OnlyYou, files.Single(f => f.FileId == mine).RestrictedTo);
        Assert.Equal(FileVisibilityRestrictions.AdminView, files.Single(f => f.FileId == theirs).RestrictedTo);
    }

    [Fact]
    public async Task A_file_in_several_visible_notes_comes_once_from_a_note_everybody_sees_first()
    {
        var fileId = Guid.NewGuid();
        var teamNote = Guid.NewGuid();
        var newerTeamNote = Guid.NewGuid();
        var repo = new FakeNoteRepository();
        repo.VisibleFiles.Add(
            Row(fileId, Guid.NewGuid(), NoteVisibility.Private, Colleague, NoteTargetType.None, null, Now.AddDays(1))
        );
        repo.VisibleFiles.Add(Row(fileId, teamNote, NoteVisibility.Team, Colleague, NoteTargetType.None, null, Now));
        repo.VisibleFiles.Add(
            Row(
                fileId,
                newerTeamNote,
                NoteVisibility.ClientVisible,
                Colleague,
                NoteTargetType.None,
                null,
                Now.AddHours(1)
            )
        );

        var file = Assert.Single(await HandleAsync(repo, [fileId], hasViewAll: true));

        Assert.Equal((newerTeamNote, (string?)null), (file.ResourceId, file.RestrictedTo));
    }

    [Fact]
    public async Task Nothing_asked_reads_nothing()
    {
        var repo = new FakeNoteRepository();

        Assert.Empty(await HandleAsync(repo, []));
        Assert.Empty(repo.VisibleFileCalls);
    }

    private static Task<IReadOnlyList<VisibleFile>> HandleAsync(
        FakeNoteRepository repo,
        IReadOnlyList<Guid> fileIds,
        bool hasViewAll = false,
        bool canViewAllCustomers = false,
        bool assignmentOn = false
    ) =>
        GetVisibleNoteFilesHandler.Handle(
            new GetVisibleNoteFilesQuery(TenantId, Actor, hasViewAll, canViewAllCustomers, fileIds),
            repo,
            Options.Create(new NotesVisibilityOptions { Enabled = assignmentOn }),
            CancellationToken.None
        );

    private static NoteAttachmentFileRow Row(
        Guid fileId,
        Guid noteId,
        NoteVisibility visibility,
        Guid author,
        NoteTargetType targetType,
        Guid? targetId,
        DateTime linkedAtUtc,
        string text = "Note"
    ) => new(fileId, noteId, text, visibility, author, targetType, targetId, linkedAtUtc);
}
