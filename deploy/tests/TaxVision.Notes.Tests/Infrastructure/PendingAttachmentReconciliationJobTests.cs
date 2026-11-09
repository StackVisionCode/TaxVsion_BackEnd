using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;
using TaxVision.Notes.Infrastructure.Jobs;
using TaxVision.Notes.Infrastructure.Persistence;

namespace TaxVision.Notes.Tests.Infrastructure;

public sealed class PendingAttachmentReconciliationJobTests
{
    // Sin abrir conexión: verifica que la consulta cross-tenant traduce a SQL Server.
    [Fact]
    public void PendingQuery_TranslatesToSqlServer()
    {
        using var db = new NotesDbContext(
            new DbContextOptionsBuilder<NotesDbContext>().UseSqlServer("Server=.;Database=translation-only").Options,
            new NoTenant()
        );

        var sql = PendingAttachmentReconciliationJob
            .PendingQuery(db, DateTime.UtcNow, DateTime.MinValue)
            .Take(10)
            .ToQueryString();

        Assert.Contains("[NoteAttachments]", sql);
        Assert.DoesNotContain("@__ef_filter", sql);
    }

    [Fact]
    public async Task PendingQuery_ReturnsOldPendingOfAnyTenant()
    {
        await using var db = new NotesDbContext(
            new DbContextOptionsBuilder<NotesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );
        var pending = NoteWithAttachment(Guid.NewGuid(), out var pendingFile);
        var available = NoteWithAttachment(Guid.NewGuid(), out var availableFile);
        available.MarkAttachmentAvailable(availableFile, DateTime.UtcNow);
        db.Notes.AddRange(pending, available);
        await db.SaveChangesAsync();

        var rows = await PendingAttachmentReconciliationJob
            .PendingQuery(db, DateTime.UtcNow.AddMinutes(1), DateTime.MinValue)
            .ToListAsync();

        var row = Assert.Single(rows);
        Assert.Equal(pending.TenantId, row.TenantId);
        Assert.Equal(pending.Id, row.NoteId);
        Assert.Equal(pendingFile, row.FileId);
    }

    private static Note NoteWithAttachment(Guid tenantId, out Guid fileId)
    {
        var note = Note.Create(
            tenantId,
            Guid.NewGuid(),
            NoteContent.Create("<p>hi</p>").Value,
            NoteReference.Create(NoteTargetType.None, null).Value,
            NoteVisibility.Private,
            null,
            DateTime.UtcNow
        ).Value;
        fileId = Guid.NewGuid();
        note.AttachFile(fileId, "receipt.pdf", "application/pdf", 1024, DateTime.UtcNow);
        return note;
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => throw new InvalidOperationException("No tenant.");
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
