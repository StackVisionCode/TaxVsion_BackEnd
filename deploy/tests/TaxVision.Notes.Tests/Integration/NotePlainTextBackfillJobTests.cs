using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;
using TaxVision.Notes.Infrastructure.Jobs;
using TaxVision.Notes.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Notes.Tests.Integration;

/// <summary>N2: el backfill llena el texto plano de notas viejas sin tocar nada más y es idempotente.</summary>
public sealed class NotePlainTextBackfillJobTests
{
    [Fact]
    public async Task Fills_plain_text_without_touching_other_fields_and_is_idempotent()
    {
        var tenantId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var notes = Enumerable
            .Range(0, 3)
            .Select(i =>
                Note.Create(
                    tenantId,
                    Guid.NewGuid(),
                    NoteContent.Create($"<p>{new string('n', 300)} <b>palabra{i}</b></p>").Value,
                    NoteReference.Create(NoteTargetType.None, null).Value,
                    NoteVisibility.Team,
                    null,
                    createdAt.AddMinutes(i)
                ).Value
            )
            .ToList();
        var ids = notes.Select(n => n.Id).ToList();
        await using (var db = NewDb())
        {
            db.Notes.AddRange(notes);
            await db.SaveChangesAsync();
            // Simula notas creadas antes de la columna.
            foreach (var id in ids)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE Notes SET ContentPlainText = NULL WHERE Id = {id}"
                );
        }
        var before = await LoadAsync(ids);
        Assert.All(before, n => Assert.Null(n.Content.PlainText));

        await using (var db = NewDb())
            await NotePlainTextBackfillJob.FillAsync(db, batchSize: 2, NullLogger.Instance, CancellationToken.None);
        var after = await LoadAsync(ids);

        foreach (var (old, filled) in before.Zip(after))
        {
            Assert.Equal($"{new string('n', 300)} palabra{ids.IndexOf(old.Id)}", filled.Content.PlainText);
            Assert.Equal(old.Content.Html, filled.Content.Html);
            Assert.Equal(old.Content.PlainTextPreview, filled.Content.PlainTextPreview);
            Assert.Equal(old.UpdatedAtUtc, filled.UpdatedAtUtc);
            Assert.Equal(old.CreatedAtUtc, filled.CreatedAtUtc);
            Assert.Equal(old.CreatedByUserId, filled.CreatedByUserId);
            Assert.Equal(old.Visibility, filled.Visibility);
            Assert.Equal(old.Status, filled.Status);
        }

        // Segunda pasada: nada que hacer, ninguna fila se reescribe.
        await using (var db = NewDb())
            await NotePlainTextBackfillJob.FillAsync(db, batchSize: 2, NullLogger.Instance, CancellationToken.None);
        var again = await LoadAsync(ids);
        Assert.Equal(after.Select(n => n.RowVersion), again.Select(n => n.RowVersion));
    }

    private static async Task<List<Note>> LoadAsync(List<Guid> ids)
    {
        await using var db = NewDb();
        var loaded = await db.Notes.IgnoreQueryFilters().AsNoTracking().Where(n => ids.Contains(n.Id)).ToListAsync();
        return loaded.OrderBy(n => ids.IndexOf(n.Id)).ToList();
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
