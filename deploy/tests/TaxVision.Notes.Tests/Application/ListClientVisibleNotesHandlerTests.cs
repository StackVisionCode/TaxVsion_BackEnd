using TaxVision.Notes.Application.Notes.Queries;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;

namespace TaxVision.Notes.Tests.Application;

/// <summary>
/// Fase 9 (03_Plan_De_Fases.md §Fase 9, aislamiento #3) — "CustomerPortal solo ve
/// <see cref="NoteVisibility.ClientVisible"/>". <see cref="PortalNotesController"/>'s solo GET no
/// tenía cobertura de handler todavía (Fase 5 solo cubrió el repo/fake); cierra ese hueco.
/// </summary>
public sealed class ListClientVisibleNotesHandlerTests
{
    private static Note MakeNote(Guid tenantId, NoteTargetType targetType, Guid targetId, NoteVisibility visibility) =>
        Note.Create(
            tenantId,
            Guid.NewGuid(),
            NoteContent.Create("<p>x</p>").Value,
            NoteReference.Create(targetType, targetId).Value,
            visibility,
            null
        ).Value;

    [Fact]
    public async Task Only_returns_ClientVisible_notes_for_the_requested_target()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var repo = new FakeNoteRepository();
        repo.Seed(MakeNote(tenantId, NoteTargetType.Customer, customerId, NoteVisibility.ClientVisible));
        repo.Seed(MakeNote(tenantId, NoteTargetType.Customer, customerId, NoteVisibility.Team));
        repo.Seed(MakeNote(tenantId, NoteTargetType.Customer, customerId, NoteVisibility.Private));

        var result = await ListClientVisibleNotesHandler.Handle(
            new ListClientVisibleNotesQuery(tenantId, NoteTargetType.Customer, customerId, 1, 20),
            repo,
            CancellationToken.None
        );

        var item = Assert.Single(result.Items);
        Assert.Equal(NoteVisibility.ClientVisible.ToString(), item.Visibility);
    }

    [Fact]
    public async Task Never_leaks_notes_from_other_targets_or_tenants()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var repo = new FakeNoteRepository();
        repo.Seed(MakeNote(tenantId, NoteTargetType.Customer, customerId, NoteVisibility.ClientVisible));
        repo.Seed(MakeNote(tenantId, NoteTargetType.Customer, otherCustomerId, NoteVisibility.ClientVisible));
        repo.Seed(MakeNote(Guid.NewGuid(), NoteTargetType.Customer, customerId, NoteVisibility.ClientVisible));

        var result = await ListClientVisibleNotesHandler.Handle(
            new ListClientVisibleNotesQuery(tenantId, NoteTargetType.Customer, customerId, 1, 20),
            repo,
            CancellationToken.None
        );

        var item = Assert.Single(result.Items);
        Assert.Equal(customerId, item.TargetId);
    }
}
