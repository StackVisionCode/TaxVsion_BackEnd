using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Infrastructure.Persistence;

namespace TaxVision.Notes.Tests.Persistence;

/// <summary>La biblioteca pregunta por adjunto con su FileId: la tabla tiene un índice por esa columna.</summary>
public sealed class NoteAttachmentFileIndexTests
{
    [Fact]
    public void Note_attachments_are_indexed_by_their_file_id()
    {
        using var db = new NotesDbContext(
            new DbContextOptionsBuilder<NotesDbContext>().UseSqlServer("Server=unused;Database=unused").Options,
            new NoTenant()
        );

        var index = db
            .Model.FindEntityType(typeof(NoteAttachment))!
            .GetIndexes()
            .SingleOrDefault(i => i.GetDatabaseName() == "IX_NoteAttachments_CloudStorageFileId");

        Assert.NotNull(index);
        Assert.Equal([nameof(NoteAttachment.CloudStorageFileId)], index.Properties.Select(p => p.Name));
        Assert.False(index.IsUnique);
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
