using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using TaxVision.CloudStorage.Tests.Integration;

namespace TaxVision.CloudStorage.Tests.Persistence;

/// <summary>El predicado de la biblioteca contra SQL Server real (base temporal de la corrida).</summary>
public sealed class LibraryReadRepositoryTests : IAsyncLifetime
{
    private static readonly LibrarySource[] AllSources =
    [
        LibrarySource.Documents,
        LibrarySource.Signature,
        LibrarySource.Billing,
        LibrarySource.Financial,
    ];

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _otherTenant = Guid.NewGuid();
    private readonly Guid _assigned = Guid.NewGuid();
    private readonly Guid _notAssigned = Guid.NewGuid();
    private readonly DateTime _now = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234567);
    private readonly Dictionary<string, Guid> _ids = [];

    public async Task InitializeAsync()
    {
        var files = new List<FileObject>
        {
            File("docA", OwnerType.Customer, _assigned, FolderType.Documents, _now, "50% off.pdf"),
            File("docB", OwnerType.Customer, _notAssigned, FolderType.Receipts, _now, "a_b.pdf"),
            File("docTenant", OwnerType.Tenant, null, FolderType.Invoices, _now.AddMinutes(-1), "axb.pdf"),
            File("templates", OwnerType.Tenant, null, FolderType.Templates, _now.AddMinutes(-2)),
            File("notes", OwnerType.Customer, _assigned, FolderType.Other, _now.AddMinutes(-3)),
            File("userDoc", OwnerType.User, Guid.NewGuid(), FolderType.Documents, _now.AddMinutes(-4)),
            Declared(
                File("sealedA", OwnerType.Customer, _assigned, FolderType.Signatures, _now.AddMinutes(-5), "same.pdf"),
                "signature_request",
                "sealed",
                null
            ),
            Declared(
                File(
                    "certNoClient",
                    OwnerType.Signature,
                    Guid.NewGuid(),
                    FolderType.Signatures,
                    _now.AddMinutes(-6),
                    "same.pdf"
                ),
                "signature_request",
                "certificate",
                null
            ),
            Declared(
                File("partial", OwnerType.Customer, _assigned, FolderType.Signatures, _now.AddMinutes(-7)),
                "signature_request",
                "partial_copy",
                null
            ),
            Declared(
                File("master", OwnerType.Signature, Guid.NewGuid(), FolderType.Signatures, _now.AddMinutes(-8)),
                "signature_request",
                "sealed_master",
                _assigned
            ),
            Declared(
                File("invoiceB", OwnerType.Invoice, Guid.NewGuid(), FolderType.Invoices, _now.AddMinutes(-9)),
                "invoice",
                "invoice_pdf",
                _notAssigned
            ),
            Declared(
                File("superseded", OwnerType.Invoice, Guid.NewGuid(), FolderType.Invoices, _now.AddMinutes(-10)),
                "invoice",
                "invoice_pdf_superseded",
                _assigned
            ),
            Declared(
                File("statementA", OwnerType.Customer, _assigned, FolderType.Documents, _now.AddMinutes(-11)),
                "financial_import_batch",
                "statement",
                null
            ),
            Declared(
                File("statementB", OwnerType.Customer, _notAssigned, FolderType.Other, _now.AddMinutes(-12)),
                "financial_import_batch",
                "statement",
                null
            ),
            File("undeclaredSignature", OwnerType.Customer, _assigned, FolderType.Signatures, _now.AddMinutes(-13)),
            Declared(
                File("original", OwnerType.Signature, Guid.NewGuid(), FolderType.Signatures, _now.AddMinutes(-16)),
                "signature_request",
                "original",
                _assigned
            ),
            Declared(
                File(
                    "originalSuperseded",
                    OwnerType.Signature,
                    Guid.NewGuid(),
                    FolderType.Signatures,
                    _now.AddMinutes(-18)
                ),
                "signature_request",
                "original_superseded",
                _assigned
            ),
            Declared(
                File("originalOnCustomer", OwnerType.Customer, _assigned, FolderType.Signatures, _now.AddMinutes(-17)),
                "signature_request",
                "original",
                null
            ),
            Declared(
                File("taskAttachment", OwnerType.Customer, _assigned, FolderType.Tasks, _now.AddMinutes(-14)),
                "task",
                "attachment",
                null
            ),
            File(
                "pending",
                OwnerType.Customer,
                _assigned,
                FolderType.Documents,
                _now.AddMinutes(-15),
                available: false
            ),
            File("otherTenant", OwnerType.Tenant, null, FolderType.Documents, _now, tenant: _otherTenant),
        };
        await using var db = NewContext();
        db.Files.AddRange(files);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db
            .Files.IgnoreQueryFilters()
            .Where(f => f.TenantId == _tenant || f.TenantId == _otherTenant)
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Only_whitelisted_available_rows_of_the_tenant_are_listed()
    {
        var rows = await Search(Access(viewAll: true));

        Assert.Equal(
            Names(
                "docA",
                "docB",
                "docTenant",
                "sealedA",
                "certNoClient",
                "original",
                "invoiceB",
                "statementA",
                "statementB"
            ),
            rows.Select(row => row.Id).ToHashSet()
        );
    }

    [Theory]
    [InlineData(LibrarySource.Documents, new[] { "docA", "docB", "docTenant" })]
    [InlineData(LibrarySource.Signature, new[] { "sealedA", "certNoClient", "original" })]
    [InlineData(LibrarySource.Billing, new[] { "invoiceB" })]
    [InlineData(LibrarySource.Financial, new[] { "statementA", "statementB" })]
    public async Task Each_source_lists_its_own_rows(LibrarySource source, string[] expected)
    {
        var rows = await Search(Access(viewAll: true, readable: [source]));

        Assert.Equal(Names(expected), rows.Select(row => row.Id).ToHashSet());
    }

    [Fact]
    public async Task Under_restriction_only_the_office_and_assigned_clients_are_listed()
    {
        var rows = await Search(Access(explorerRestrict: true));

        Assert.Equal(
            Names("docA", "docTenant", "sealedA", "original", "statementA"),
            rows.Select(row => row.Id).ToHashSet()
        );
    }

    [Fact]
    public async Task With_the_flag_off_only_financial_is_restricted()
    {
        var rows = await Search(Access(explorerRestrict: false));

        Assert.Equal(
            Names("docA", "docB", "docTenant", "sealedA", "certNoClient", "original", "invoiceB", "statementA"),
            rows.Select(row => row.Id).ToHashSet()
        );
    }

    [Theory]
    [InlineData("50%", "docA")]
    [InlineData("a_b", "docB")]
    public async Task Wildcards_in_the_search_are_literal(string q, string expected)
    {
        var rows = await Search(Access(viewAll: true), new LibrarySearchRequest { Q = q });

        Assert.Equal(_ids[expected], Assert.Single(rows).Id);
    }

    [Theory]
    [InlineData("newest")]
    [InlineData("oldest")]
    [InlineData("name")]
    public async Task Keyset_pages_neither_skip_nor_repeat(string sort)
    {
        var access = Access(viewAll: true);
        var all = await Search(access, new LibrarySearchRequest { Sort = sort });
        var paged = new List<LibraryRow>();
        string? cursor = null;
        do
        {
            var page = await Search(access, new LibrarySearchRequest { Sort = sort, Cursor = cursor }, take: 3);
            paged.AddRange(page);
            cursor =
                page.Count == 3
                    ? LibraryCursor
                        .After(
                            LibraryFilter.Create(new() { Sort = sort }).Value.Sort,
                            page[^1].CreatedAtUtc,
                            page[^1].Name,
                            page[^1].Id
                        )
                        .Encode()
                    : null;
        } while (cursor is not null);

        Assert.Equal(9, all.Count);
        Assert.Equal(all.Select(row => row.Id), paged.Select(row => row.Id));
    }

    [Fact]
    public async Task Counts_match_the_listed_rows()
    {
        var access = Access(explorerRestrict: false);
        await using var db = NewContext();

        var counts = await new LibraryReadRepository(db).CountBySourceAsync(
            _tenant,
            LibraryAccessPolicy.ScopeFor(access, access.Readable),
            LibraryFilter.Create(new LibrarySearchRequest()).Value,
            CancellationToken.None
        );

        Assert.Equal(3, counts[LibrarySource.Documents]);
        Assert.Equal(3, counts[LibrarySource.Signature]);
        Assert.Equal(1, counts[LibrarySource.Billing]);
        Assert.Equal(1, counts[LibrarySource.Financial]);
    }

    [Fact]
    public async Task Find_uses_the_same_predicate()
    {
        var access = Access(explorerRestrict: true);
        var scope = LibraryAccessPolicy.ScopeFor(access, access.Readable);
        await using var db = NewContext();
        var repository = new LibraryReadRepository(db);

        Assert.NotNull(await repository.FindAsync(_tenant, scope, _ids["docA"], CancellationToken.None));
        Assert.Null(await repository.FindAsync(_tenant, scope, _ids["docB"], CancellationToken.None));
        Assert.Null(await repository.FindAsync(_tenant, scope, _ids["templates"], CancellationToken.None));
        Assert.Null(await repository.FindAsync(_tenant, scope, _ids["otherTenant"], CancellationToken.None));
        Assert.Null(await repository.FindAsync(_otherTenant, scope, _ids["docA"], CancellationToken.None));
    }

    [Fact]
    public async Task The_customer_filter_uses_the_related_customer()
    {
        var rows = await Search(Access(viewAll: true), new LibrarySearchRequest { CustomerId = _notAssigned });

        Assert.Equal(Names("docB", "invoiceB", "statementB"), rows.Select(row => row.Id).ToHashSet());
    }

    private async Task<IReadOnlyList<LibraryRow>> Search(
        LibraryAccess access,
        LibrarySearchRequest? request = null,
        int take = 100
    )
    {
        await using var db = NewContext();
        return await new LibraryReadRepository(db).SearchAsync(
            _tenant,
            LibraryAccessPolicy.ScopeFor(access, access.Readable),
            LibraryFilter.Create(request ?? new LibrarySearchRequest()).Value,
            take,
            CancellationToken.None
        );
    }

    private LibraryAccess Access(
        bool viewAll = false,
        bool explorerRestrict = false,
        LibrarySource[]? readable = null
    ) => new(readable ?? AllSources, true, viewAll, explorerRestrict, viewAll ? [] : [_assigned]);

    private HashSet<Guid> Names(params string[] names) => names.Select(name => _ids[name]).ToHashSet();

    private FileObject File(
        string label,
        OwnerType owner,
        Guid? ownerId,
        FolderType folder,
        DateTime createdAtUtc,
        string? name = null,
        bool available = true,
        Guid? tenant = null
    )
    {
        var tenantId = tenant ?? _tenant;
        var id = Guid.NewGuid();
        _ids[label] = id;
        var key = ObjectKey.Create($"tenants/{tenantId:N}/{owner}/files/2025/{id:N}.pdf").Value;
        var file = FileObject
            .Register(
                id,
                tenantId,
                owner,
                ownerId,
                folder,
                2025,
                key,
                name ?? $"{label}.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                createdAtUtc,
                createdAtUtc.AddHours(1)
            )
            .Value;
        if (!available)
            return file;
        file.MarkPendingScan(createdAtUtc);
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", createdAtUtc);
        return file;
    }

    private static FileObject Declared(FileObject file, string resourceType, string role, Guid? relatedCustomerId)
    {
        file.DeclareSource(
            FileSourceDeclaration.Create("tests", resourceType, Guid.NewGuid(), role, relatedCustomerId).Value,
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
