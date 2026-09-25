using System.Security.Claims;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.ResourceAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using TaxVision.Notes.Api.Authorization;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;
using TaxVision.Notes.Tests.Application;
using Xunit;

namespace TaxVision.Notes.Tests.Api;

/// <summary>
/// Capa 2 (Fase 9) del manage-override del punto 3.2: el handler aditivo deja EDITAR
/// (<see cref="Operations.Update"/>) una nota de un autor RETIRADO a un staff con
/// <c>notes.view_all</c>, sin relajar el resto (autor no retirado / sin view_all / otra operación).
/// </summary>
public sealed class OffboardedAuthorNoteEditHandlerTests
{
    private static Note NewNote(Guid tenantId, Guid authorId) =>
        Note.Create(
            tenantId,
            authorId,
            NoteContent.Create("<p>x</p>").Value,
            NoteReference.Create(NoteTargetType.None, null).Value,
            NoteVisibility.Team,
            null
        ).Value;

    private static ClaimsPrincipal User(Guid userId) =>
        new(new ClaimsIdentity([new Claim("sub", userId.ToString())], "Test"));

    private static async Task<bool> RunAsync(
        Note note,
        Guid actingUserId,
        bool hasViewAll,
        bool authorOffboarded,
        OperationAuthorizationRequirement requirement
    )
    {
        var offboarded = new FakeOffboardedStaffRepository();
        if (authorOffboarded)
            offboarded.MarkOffboarded(note.TenantId, note.CreatedByUserId);

        var handler = new OffboardedAuthorNoteEditHandler(new StubPermissions(hasViewAll), offboarded);
        var context = new AuthorizationHandlerContext([requirement], User(actingUserId), note);
        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task Succeeds_for_a_view_all_staff_editing_an_offboarded_authors_note()
    {
        var note = NewNote(Guid.NewGuid(), Guid.NewGuid());
        Assert.True(await RunAsync(note, Guid.NewGuid(), hasViewAll: true, authorOffboarded: true, Operations.Update));
    }

    [Fact]
    public async Task Does_not_succeed_when_the_author_is_not_offboarded()
    {
        var note = NewNote(Guid.NewGuid(), Guid.NewGuid());
        Assert.False(
            await RunAsync(note, Guid.NewGuid(), hasViewAll: true, authorOffboarded: false, Operations.Update)
        );
    }

    [Fact]
    public async Task Does_not_succeed_without_view_all_even_if_the_author_was_offboarded()
    {
        var note = NewNote(Guid.NewGuid(), Guid.NewGuid());
        Assert.False(
            await RunAsync(note, Guid.NewGuid(), hasViewAll: false, authorOffboarded: true, Operations.Update)
        );
    }

    [Fact]
    public async Task Does_not_fire_for_the_author_themselves()
    {
        var authorId = Guid.NewGuid();
        var note = NewNote(Guid.NewGuid(), authorId);
        Assert.False(await RunAsync(note, authorId, hasViewAll: true, authorOffboarded: true, Operations.Update));
    }

    [Fact]
    public async Task Does_not_apply_to_operations_other_than_update()
    {
        var note = NewNote(Guid.NewGuid(), Guid.NewGuid());
        Assert.False(await RunAsync(note, Guid.NewGuid(), hasViewAll: true, authorOffboarded: true, Operations.Delete));
    }

    private sealed class StubPermissions(bool hasViewAll) : IUserPermissionsSource
    {
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default) =>
            Task.FromResult(hasViewAll && permission == NotesPermissions.ViewAll);
    }
}
