using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using TaxVision.CloudStorage.Tests.Integration;

namespace TaxVision.CloudStorage.Tests.Persistence;

/// <summary>Las tres ramas por fila del predicado contra SQL Server real: solo candidatos, sin recorte por cliente.</summary>
public sealed class LibraryReadRepositoryRowSourcesTests : IAsyncLifetime
{
    private static readonly LibrarySource[] RowSources = [LibrarySource.Tasks, LibrarySource.Forms, LibrarySource.Mail];

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _assigned = Guid.NewGuid();
    private readonly Guid _notAssigned = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private readonly Dictionary<string, Guid> _ids = [];
    private int _minute;

    public async Task InitializeAsync()
    {
        var files = new List<FileObject>
        {
            Declared(File("taskAssigned", OwnerType.Customer, _assigned, FolderType.Tasks), "task", "attachment"),
            Declared(File("taskNotAssigned", OwnerType.Customer, _notAssigned, FolderType.Tasks), "task", "attachment"),
            Declared(File("taskUser", OwnerType.User, Guid.NewGuid(), FolderType.Tasks), "task", "attachment"),
            Declared(
                File("taskInDocuments", OwnerType.Customer, _assigned, FolderType.Documents),
                "task",
                "attachment"
            ),
            Declared(
                File("formAttachment", OwnerType.Customer, _notAssigned, FolderType.ClientForms),
                "form_request",
                "attachment"
            ),
            Declared(
                File("formSummary", OwnerType.Customer, _assigned, FolderType.ClientForms),
                "form_request",
                "summary_pdf"
            ),
            Declared(
                File("formParticipant", OwnerType.FormParticipant, Guid.NewGuid(), FolderType.ClientForms),
                "form_request",
                "attachment"
            ),
            Declared(
                File("formUnknownRole", OwnerType.Customer, _assigned, FolderType.ClientForms),
                "form_request",
                "signing_copy"
            ),
            Declared(
                File("mailCustomer", OwnerType.Customer, _notAssigned, FolderType.EmailOutgoing),
                "email_message",
                "outgoing_attachment"
            ),
            Declared(
                File("mailUser", OwnerType.User, Guid.NewGuid(), FolderType.EmailOutgoing),
                "email_message",
                "outgoing_attachment"
            ),
            Declared(
                File("mailIncomingCustomer", OwnerType.Customer, _notAssigned, FolderType.EmailIncoming),
                "incoming_email",
                "incoming_attachment"
            ),
            Declared(
                File("mailIncomingUser", OwnerType.User, Guid.NewGuid(), FolderType.EmailIncoming),
                "incoming_email",
                "incoming_attachment"
            ),
            Declared(
                File("mailIncomingInOutgoing", OwnerType.Customer, _assigned, FolderType.EmailOutgoing),
                "incoming_email",
                "incoming_attachment"
            ),
            File("undeclaredTasks", OwnerType.Customer, _assigned, FolderType.Tasks),
            File("docAssigned", OwnerType.Customer, _assigned, FolderType.Documents),
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
    public async Task A_restricted_employee_gets_every_candidate_including_unassigned_clients()
    {
        var rows = await Search(Restricted(RowSources));

        Assert.Equal(
            Names(
                "taskAssigned",
                "taskNotAssigned",
                "taskUser",
                "formAttachment",
                "formSummary",
                "formParticipant",
                "mailCustomer",
                "mailUser",
                "mailIncomingCustomer",
                "mailIncomingUser"
            ),
            rows.Select(row => row.Id).ToHashSet()
        );
    }

    [Theory]
    [InlineData(LibrarySource.Tasks, new[] { "taskAssigned", "taskNotAssigned", "taskUser" })]
    [InlineData(LibrarySource.Forms, new[] { "formAttachment", "formSummary", "formParticipant" })]
    [InlineData(LibrarySource.Mail, new[] { "mailCustomer", "mailUser", "mailIncomingCustomer", "mailIncomingUser" })]
    public async Task Each_row_scoped_source_lists_its_candidates(LibrarySource source, string[] expected)
    {
        var rows = await Search(Restricted([source]));

        Assert.Equal(Names(expected), rows.Select(row => row.Id).ToHashSet());
    }

    [Fact]
    public async Task Row_scoped_candidates_mix_with_sql_sources_under_restriction()
    {
        var rows = await Search(Restricted([LibrarySource.Documents, LibrarySource.Tasks]));

        Assert.Equal(
            Names("taskAssigned", "taskNotAssigned", "taskUser", "docAssigned"),
            rows.Select(row => row.Id).ToHashSet()
        );
    }

    [Fact]
    public async Task Find_returns_a_candidate_of_an_unassigned_client()
    {
        var access = Restricted(RowSources);
        await using var db = NewContext();

        var row = await new LibraryReadRepository(db).FindAsync(
            _tenant,
            LibraryAccessPolicy.ScopeFor(access, access.Readable),
            _ids["mailCustomer"],
            CancellationToken.None
        );

        Assert.NotNull(row);
        Assert.Equal(LibrarySource.Mail, LibrarySourceCatalog.Classify(row));
    }

    [Fact]
    public async Task Row_scoped_sources_are_not_counted()
    {
        var access = Restricted([LibrarySource.Documents, .. RowSources]);
        await using var db = NewContext();

        var counts = await new LibraryReadRepository(db).CountBySourceAsync(
            _tenant,
            LibraryAccessPolicy.ScopeFor(access, access.Readable),
            LibraryFilter.Create(new LibrarySearchRequest()).Value,
            CancellationToken.None
        );

        Assert.Equal(1, counts[LibrarySource.Documents]);
        Assert.Equal([LibrarySource.Documents], counts.Keys);
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

    // Empleado restringido: flag encendido, sin view_all, un solo cliente asignado.
    private LibraryAccess Restricted(LibrarySource[] readable) => new(readable, true, false, true, [_assigned]);

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

    private static FileObject Declared(FileObject file, string resourceType, string role)
    {
        file.DeclareSource(
            FileSourceDeclaration.Create("tests", resourceType, Guid.NewGuid(), role, null).Value,
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
