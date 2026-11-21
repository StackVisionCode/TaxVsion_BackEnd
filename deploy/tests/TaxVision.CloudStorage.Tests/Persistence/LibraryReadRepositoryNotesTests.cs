using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using TaxVision.CloudStorage.Tests.Integration;

namespace TaxVision.CloudStorage.Tests.Persistence;

/// <summary>
/// Rama SQL de <c>notes</c> contra SQL Server real: solo adjuntos declarados por Notes, con dueño cliente u oficina y
/// carpeta Other. Sin recorte por cliente asignado: lo decide Notes por fila.
/// </summary>
public sealed class LibraryReadRepositoryNotesTests : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private readonly Dictionary<string, Guid> _ids = [];
    private int _minute;

    public async Task InitializeAsync()
    {
        var files = new List<FileObject>
        {
            Declared(File("customerNote", OwnerType.Customer, Guid.NewGuid(), FolderType.Other), "note"),
            Declared(File("officeNote", OwnerType.Tenant, null, FolderType.Other), "note"),
            File("undeclared", OwnerType.Customer, Guid.NewGuid(), FolderType.Other),
            Declared(File("userOther", OwnerType.User, Guid.NewGuid(), FolderType.Other), "note"),
            Declared(File("inDocuments", OwnerType.Customer, Guid.NewGuid(), FolderType.Documents), "note"),
            Declared(File("chat", OwnerType.Communication, Guid.NewGuid(), FolderType.Other), "conversation"),
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
    public async Task Only_declared_note_attachments_of_a_customer_or_the_office_are_candidates()
    {
        var rows = await Search(new([LibrarySource.Notes], false, true, false, []));

        Assert.Equal(Names("customerNote", "officeNote"), rows.Select(row => row.Id).ToHashSet());
        Assert.All(rows, row => Assert.Equal(LibrarySource.Notes, LibrarySourceCatalog.Classify(row)));
    }

    [Fact]
    public async Task A_restricted_viewer_still_gets_every_candidate()
    {
        var rows = await Search(new([LibrarySource.Notes], false, false, true, []));

        Assert.Equal(Names("customerNote", "officeNote"), rows.Select(row => row.Id).ToHashSet());
    }

    [Fact]
    public async Task Without_notes_in_scope_nothing_comes()
    {
        Assert.Empty(await Search(new([LibrarySource.Documents], false, false, true, [])));
    }

    private async Task<IReadOnlyList<LibraryRow>> Search(LibraryAccess access)
    {
        await using var db = NewContext();
        return await new LibraryReadRepository(db).SearchAsync(
            _tenant,
            LibraryAccessPolicy.ScopeFor(access, access.Readable),
            LibraryFilter.Create(new LibrarySearchRequest()).Value,
            100,
            CancellationToken.None
        );
    }

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

    private static FileObject Declared(FileObject file, string resourceType)
    {
        var service = resourceType == "note" ? "notes" : "communication";
        file.DeclareSource(
            FileSourceDeclaration.Create(service, resourceType, Guid.NewGuid(), "attachment", null).Value,
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
