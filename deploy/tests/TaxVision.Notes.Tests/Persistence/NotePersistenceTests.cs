using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;
using TaxVision.Notes.Infrastructure.Persistence;
using TaxVision.Notes.Infrastructure.Persistence.Repositories;

namespace TaxVision.Notes.Tests.Persistence;

/// <summary>
/// Checkpoint 2 de 03_Plan_De_Fases.md: verifica que el mapeo EF de <see cref="Note"/> (owned
/// types Content/Reference/Color + child table Attachments vía backing field) persiste y recarga
/// correctamente, y que el filtro global fail-closed por tenant (RBAC Fase 5, replicado en
/// <see cref="NotesDbContext"/>) aísla las notas de otro tenant.
/// </summary>
public sealed class NotePersistenceTests
{
    // Mismo patrón que IncomingEmailPersistenceTests/DraftRepositoryTests (Correspondence): un
    // ITenantContext falso mutable, seteado por test para simular lo que en producción hace
    // JwtTenantContextMiddleware.
    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private static NotesDbContext CreateContext(string databaseName, ITenantContext tenantContext) =>
        new(new DbContextOptionsBuilder<NotesDbContext>().UseInMemoryDatabase(databaseName).Options, tenantContext);

    private static Note NewNote(Guid tenantId, Guid authorUserId, Guid customerId) =>
        Note.Create(
            tenantId,
            authorUserId,
            NoteContent.Create("<p>Llamar al cliente mañana.</p>").Value,
            NoteReference.Create(NoteTargetType.Customer, customerId).Value,
            NoteVisibility.Team,
            NoteColor.Create(NoteColorKind.FollowUp).Value
        ).Value;

    [Fact]
    public async Task Note_with_content_reference_color_and_attachment_persists_and_reloads_correctly()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var authorUserId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);
        Guid noteId;

        await using (var db = CreateContext(databaseName, tenantContext))
        {
            var note = NewNote(tenantId, authorUserId, customerId);
            noteId = note.Id;
            Assert.True(note.AttachFile(Guid.NewGuid(), "contrato.pdf", "application/pdf", 4096).IsSuccess);

            db.Notes.Add(note);
            await db.SaveChangesAsync();

            // Verifica que Attachments quedó en su propia tabla (child row real vía backing
            // field), no descartado por EF ni serializado — guardrail 9.
            Assert.Equal(1, await db.NoteAttachments.CountAsync(a => a.NoteId == noteId));
        }

        await using var reloadDb = CreateContext(databaseName, tenantContext);
        var reloaded = await reloadDb.Notes.Include(n => n.Attachments).SingleAsync(n => n.Id == noteId);

        Assert.Equal("<p>Llamar al cliente mañana.</p>", reloaded.Content.Html);
        Assert.Equal(NoteTargetType.Customer, reloaded.Reference.TargetType);
        Assert.Equal(customerId, reloaded.Reference.TargetId);
        Assert.Equal(NoteColorKind.FollowUp, reloaded.Color?.Kind);
        Assert.Single(reloaded.Attachments);
        Assert.Equal("contrato.pdf", reloaded.Attachments.Single().DisplayName);
        Assert.Equal(NoteAttachmentStatus.Pending, reloaded.Attachments.Single().Status);
    }

    [Fact]
    public async Task Global_tenant_filter_hides_notes_belonging_to_a_different_tenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();

        tenantContext.SetTenant(ownerTenantId);
        await using (var writeDb = CreateContext(databaseName, tenantContext))
        {
            writeDb.Notes.Add(NewNote(ownerTenantId, Guid.NewGuid(), Guid.NewGuid()));
            await writeDb.SaveChangesAsync();
        }

        // Mismo proceso, mismo modelo compilado — solo cambia el tenant efectivo de ESTA
        // instancia de DbContext (ver comentario de EffectiveTenantId en NotesDbContext).
        tenantContext.SetTenant(otherTenantId);
        await using var readDb = CreateContext(databaseName, tenantContext);

        Assert.Empty(await readDb.Notes.ToListAsync());
    }

    [Fact]
    public async Task NoteRepository_GetByIdAsync_returns_the_note_only_for_its_own_tenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);

        await using var db = CreateContext(databaseName, tenantContext);
        var repository = new NoteRepository(db);
        var note = NewNote(tenantId, Guid.NewGuid(), Guid.NewGuid());
        await repository.AddAsync(note);
        await db.SaveChangesAsync();

        var ownTenantResult = await repository.GetByIdAsync(tenantId, note.Id);
        var otherTenantResult = await repository.GetByIdAsync(otherTenantId, note.Id);

        Assert.NotNull(ownTenantResult);
        Assert.Null(otherTenantResult);
    }
}
