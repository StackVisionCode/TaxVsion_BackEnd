using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;
using TaxVision.Notes.Infrastructure.Persistence;
using TaxVision.Notes.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Notes.Tests.Integration;

/// <summary>
/// NTS-5 con SQL real, en una base temporal propia. Adjuntar agrega una fila hija: si no tocara la raíz, dos adjuntos
/// a la vez no chocarían y el tope de 20 se pasaría.
/// </summary>
public sealed class NoteConcurrencyIntegrationTests : IAsyncLifetime
{
    private const string ApiUserSecretsId = "d61a76cb-cc40-479a-a7a3-a0f76548ef8c";
    private const string Prefix = "ntsint_";

    private readonly string _connectionString;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _author = Guid.NewGuid();

    public NoteConcurrencyIntegrationTests()
    {
        var baseConnection =
            Environment.GetEnvironmentVariable("NOTES_DB_CONNECTION")
            ?? new ConfigurationBuilder().AddUserSecrets(ApiUserSecretsId).Build().GetConnectionString("Default")
            ?? throw new InvalidOperationException("Set NOTES_DB_CONNECTION or the Notes user-secrets.");
        _connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
        }.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        await DropLeftoversAsync();
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task TwoAttachmentsOnNoteWith19_SecondConflicts()
    {
        var noteId = await SeedNoteAsync(attachments: 19);

        await AssertSecondConflictsAsync(noteId, Attach, Attach);

        await using var read = NewContext();
        var active = await read.Set<NoteAttachment>()
            .IgnoreQueryFilters()
            .CountAsync(a => a.NoteId == noteId && a.Status != NoteAttachmentStatus.Detached);
        Assert.Equal(20, active);
    }

    [Fact]
    public async Task ConcurrentContentEdits_SecondConflicts()
    {
        var noteId = await SeedNoteAsync(attachments: 0);

        await AssertSecondConflictsAsync(
            noteId,
            n => n.UpdateContent(NoteContent.Create("<p>First</p>").Value, _author, DateTime.UtcNow),
            n => n.UpdateContent(NoteContent.Create("<p>Second</p>").Value, _author, DateTime.UtcNow)
        );

        await using var read = NewContext();
        var stored = await read.Notes.IgnoreQueryFilters().SingleAsync(n => n.Id == noteId);
        Assert.Equal("<p>First</p>", stored.Content.Html);
    }

    /// <summary>El veredicto del escáner llega por mensaje: si choca con una edición, se reintenta y no se pierde.</summary>
    [Fact]
    public async Task ScanVerdictWhileEditing_SecondConflicts()
    {
        var fileId = Guid.NewGuid();
        var noteId = await SeedNoteAsync(attachments: 0, fileId);

        await AssertSecondConflictsAsync(
            noteId,
            n => n.UpdateContent(NoteContent.Create("<p>Edited</p>").Value, _author, DateTime.UtcNow),
            n => n.MarkAttachmentAvailable(fileId, DateTime.UtcNow)
        );
    }

    /// <summary>NTS-6 en SQL real: el mismo archivo en dos notas llega con las dos, con sus adjuntos.</summary>
    [Fact]
    public async Task ListByAttachmentFileId_BringsEveryNoteWithTheFile()
    {
        var fileId = Guid.NewGuid();
        var first = await SeedNoteAsync(attachments: 0, fileId);
        var second = await SeedNoteAsync(attachments: 0, fileId);
        await SeedNoteAsync(attachments: 1);

        await using var db = NewContext();
        var notes = await new NoteRepository(db).ListByAttachmentFileIdAsync(fileId);

        Assert.Equal(new[] { first, second }.Order(), notes.Select(n => n.Id).Order());
        Assert.All(notes, n => Assert.Single(n.Attachments));
    }

    private async Task AssertSecondConflictsAsync(Guid noteId, Func<Note, Result> first, Func<Note, Result> second)
    {
        await using var winnerDb = NewContext();
        await using var loserDb = NewContext();
        var winner = await LoadAsync(winnerDb, noteId);
        var loser = await LoadAsync(loserDb, noteId);

        Assert.True(first(winner).IsSuccess);
        Assert.True(second(loser).IsSuccess);

        await winnerDb.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => loserDb.SaveChangesAsync());
        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
    }

    private static Task<Note> LoadAsync(NotesDbContext db, Guid noteId) =>
        db.Notes.IgnoreQueryFilters().Include(n => n.Attachments).SingleAsync(n => n.Id == noteId);

    private static Result Attach(Note note) => Attach(note, Guid.NewGuid());

    private static Result Attach(Note note, Guid fileId) => note.AttachFile(fileId, "file.pdf", "application/pdf", 100, DateTime.UtcNow);

    private async Task<Guid> SeedNoteAsync(int attachments, Guid? pendingFileId = null)
    {
        var note = Note.Create(
            _tenantId,
            _author,
            NoteContent.Create("<p>Hello</p>").Value,
            NoteReference.Create(NoteTargetType.Customer, Guid.NewGuid()).Value,
            NoteVisibility.Team,
            null,
            DateTime.UtcNow
        ).Value;
        for (var i = 0; i < attachments; i++)
            Attach(note);
        if (pendingFileId is { } fileId)
            Attach(note, fileId);

        await using var db = NewContext();
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        return note.Id;
    }

    private NotesDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<NotesDbContext>().UseSqlServer(_connectionString).Options,
            new NoTenantContext()
        );

    // Solo las de más de una hora: las recientes pueden ser de otra prueba corriendo ahora.
    private async Task DropLeftoversAsync()
    {
        var master = new SqlConnectionStringBuilder(_connectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER DATABASE ' + QUOTENAME(name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(name) + N';'
              FROM sys.databases WHERE name LIKE N'{Prefix}%' AND create_date < DATEADD(HOUR, -1, GETDATE());
            EXEC sp_executesql @sql;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;

        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
