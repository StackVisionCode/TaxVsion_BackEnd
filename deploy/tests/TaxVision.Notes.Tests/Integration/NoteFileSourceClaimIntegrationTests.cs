using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;
using TaxVision.Notes.Infrastructure.Notes;
using TaxVision.Notes.Infrastructure.Persistence;
using TaxVision.Notes.Tests.Application;

namespace TaxVision.Notes.Tests.Integration;

/// <summary>
/// Búsqueda de adjuntos por FileId para el reclamo de orígenes, traducida por SQL Server: solo esa oficina, solo esos
/// ids y las mismas reglas que la reposición.
/// </summary>
public sealed class NoteFileSourceClaimIntegrationTests
{
    private static readonly DateTime BaseUtc = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly Guid tenantId = Guid.NewGuid();
    private readonly Guid author = Guid.NewGuid();

    [Fact]
    public async Task Only_the_claimed_live_attachments_of_the_office_are_declared()
    {
        var customer = Guid.NewGuid();
        var claimed = Guid.NewGuid();
        var notClaimed = Guid.NewGuid();
        var detached = Guid.NewGuid();
        var onDeleted = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var note = await SeedAsync(
            tenantId,
            [claimed, notClaimed, detached],
            NoteTargetType.Customer,
            customer,
            configure: n => n.DetachFile(detached, BaseUtc)
        );
        await SeedAsync(tenantId, [onDeleted], delete: true);
        await SeedAsync(Guid.NewGuid(), [foreign]);

        await using var db = NewDb();
        var (outbox, _) = RedeclareFileSourcesTests.RecordingOutbox.Create(db);
        var declarations = await new NoteFileSourceRedeclarer(
            outbox,
            new NoOpCorrelationContext()
        ).DeclarationsForAsync(tenantId, [claimed, detached, onDeleted, foreign, Guid.NewGuid()], "claim", default);

        var declared = Assert.Single(declarations);
        Assert.Equal(
            (tenantId, claimed, note.Id, (Guid?)customer, "claim"),
            (
                declared.TenantId,
                declared.FileId,
                declared.SourceResourceId,
                declared.RelatedCustomerId,
                declared.CorrelationId
            )
        );
    }

    private async Task<Note> SeedAsync(
        Guid tenant,
        Guid[] files,
        NoteTargetType targetType = NoteTargetType.None,
        Guid? targetId = null,
        bool delete = false,
        Action<Note>? configure = null
    )
    {
        var note = Note.Create(
            tenant,
            author,
            NoteContent.Create("<p>nota</p>").Value,
            NoteReference.Create(targetType, targetId).Value,
            NoteVisibility.Team,
            null,
            BaseUtc
        ).Value;
        foreach (var file in files)
            Assert.True(note.AttachFile(file, "a.pdf", "application/pdf", 10, BaseUtc).IsSuccess);
        configure?.Invoke(note);
        if (delete)
            Assert.True(note.SoftDelete(author, BaseUtc.AddMinutes(1)).IsSuccess);

        await using var db = NewDb();
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        return note;
    }

    private static NotesDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<NotesDbContext>()
                .UseSqlServer(NotesIntegrationDatabase.ConnectionString)
                .Options,
            new NoTenant()
        );

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
