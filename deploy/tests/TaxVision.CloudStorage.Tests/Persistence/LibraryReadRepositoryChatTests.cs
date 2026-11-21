using BuildingBlocks.Contracts.Files;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using TaxVision.CloudStorage.Tests.Integration;

namespace TaxVision.CloudStorage.Tests.Persistence;

/// <summary>Rama SQL de <c>chat</c> contra SQL Server real: solo candidatos de las conversaciones del ámbito.</summary>
public sealed class LibraryReadRepositoryChatTests : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _mine = Guid.NewGuid();
    private readonly Guid _notMine = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private readonly Dictionary<string, Guid> _ids = [];
    private int _minute;

    public async Task InitializeAsync()
    {
        var files = new List<FileObject>
        {
            Declared(File("attachment", OwnerType.Communication, _mine, FolderType.Other), _mine, "attachment"),
            Declared(File("otherChat", OwnerType.Communication, _notMine, FolderType.Other), _notMine, "attachment"),
            Declared(File("voiceNote", OwnerType.Communication, _mine, FolderType.VoiceNotes), _mine, "voice_note"),
            // Nota de voz antigua: dueño Tenant sin id, carpeta VoiceNotes.
            Declared(File("oldVoiceNote", OwnerType.Tenant, null, FolderType.VoiceNotes), _mine, "voice_note"),
            Declared(
                File("customerDocument", OwnerType.Customer, Guid.NewGuid(), FolderType.Other),
                _mine,
                "attachment"
            ),
            Declared(
                File("transcript", OwnerType.Communication, Guid.NewGuid(), FolderType.Transcripts),
                _mine,
                "transcript"
            ),
            File("undeclared", OwnerType.Communication, _mine, FolderType.Other),
        };
        await using var db = NewContext();
        db.Files.AddRange(files);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Files.IgnoreQueryFilters().Where(f => f.TenantId == _tenant).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Only_files_of_the_conversations_in_scope_are_candidates()
    {
        var rows = await Search(Scope([_mine]));

        Assert.Equal(Names("attachment", "voiceNote", "oldVoiceNote"), rows.Select(row => row.Id).ToHashSet());
        Assert.All(rows, row => Assert.Equal(LibrarySource.Chat, LibrarySourceCatalog.Classify(row)));
    }

    [Fact]
    public async Task An_empty_scope_brings_nothing()
    {
        Assert.Empty(await Search(Scope([])));
    }

    [Fact]
    public async Task A_scope_of_thousands_of_conversations_goes_as_one_parameter()
    {
        var many = Enumerable.Range(0, 2_500).Select(_ => Guid.NewGuid()).Append(_notMine).ToList();

        var rows = await Search(Scope(many));

        Assert.Equal(Names("otherChat"), rows.Select(row => row.Id).ToHashSet());
    }

    [Fact]
    public async Task Without_a_scope_the_download_lookup_still_finds_the_candidate()
    {
        var access = Access();
        await using var db = NewContext();

        var row = await new LibraryReadRepository(db).FindAsync(
            _tenant,
            LibraryAccessPolicy.ScopeFor(access, access.Readable),
            _ids["otherChat"],
            CancellationToken.None
        );

        Assert.NotNull(row);
        Assert.Equal(LibrarySource.Chat, LibrarySourceCatalog.Classify(row));
    }

    [Fact]
    public async Task Chat_rows_are_not_counted()
    {
        await using var db = NewContext();

        var counts = await new LibraryReadRepository(db).CountBySourceAsync(
            _tenant,
            Scope([_mine]),
            LibraryFilter.Create(new LibrarySearchRequest()).Value,
            CancellationToken.None
        );

        Assert.Empty(counts);
    }

    private async Task<IReadOnlyList<LibraryRow>> Search(LibraryReadScope scope)
    {
        await using var db = NewContext();
        return await new LibraryReadRepository(db).SearchAsync(
            _tenant,
            scope,
            LibraryFilter.Create(new LibrarySearchRequest()).Value,
            100,
            CancellationToken.None
        );
    }

    private static LibraryAccess Access() => new([LibrarySource.Chat], true, false, true, []);

    private static LibraryReadScope Scope(IReadOnlyList<Guid> conversations) =>
        LibraryAccessPolicy.ScopeFor(
            Access(),
            [LibrarySource.Chat],
            new CommunicationFileScope(conversations, [], [], [])
        );

    private HashSet<Guid> Names(params string[] names) => names.Select(name => _ids[name]).ToHashSet();

    private FileObject File(string label, OwnerType owner, Guid? ownerId, FolderType folder)
    {
        var id = Guid.NewGuid();
        _ids[label] = id;
        var createdAtUtc = _now.AddMinutes(-++_minute);
        var key = ObjectKey.Create($"tenants/{_tenant:N}/{owner}/files/2025/{id:N}.pdf").Value;
        var file = FileObject
            .Register(
                id,
                _tenant,
                owner,
                ownerId,
                folder,
                2025,
                key,
                $"{label}.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                createdAtUtc,
                createdAtUtc.AddHours(1)
            )
            .Value;
        file.MarkPendingScan(createdAtUtc);
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", createdAtUtc);
        return file;
    }

    private static FileObject Declared(FileObject file, Guid conversationId, string role)
    {
        var resourceType = role == "transcript" ? "call" : "conversation";
        file.DeclareSource(
            FileSourceDeclaration.Create("communication", resourceType, conversationId, role, null).Value,
            DateTime.UtcNow
        );
        return file;
    }

    private CloudStorageDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CloudStorageDbContext>()
                .UseSqlServer(CloudStorageIntegrationDatabase.ConnectionString)
                .Options,
            new FixedTenant(_tenant)
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
