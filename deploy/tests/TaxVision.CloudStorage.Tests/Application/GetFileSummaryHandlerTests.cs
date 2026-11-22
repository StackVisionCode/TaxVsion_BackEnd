using BuildingBlocks.Contracts.Files;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TimeZones;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// Resumen del widget Documents: solo la rama Documents de la biblioteca, con su recorte por cliente y en días de
/// la oficina. Lector real sobre InMemory.
/// </summary>
public sealed class GetFileSummaryHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 15, 0, 0, DateTimeKind.Utc);
    private static readonly ResolvedTimeZone Chicago = ResolvedTimeZone.Resolve("America/Chicago");
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _customerA = Guid.NewGuid();
    private readonly Guid _customerB = Guid.NewGuid();
    private readonly Guid _employee = Guid.NewGuid();

    [Fact]
    public async Task Counts_only_documents_the_actor_can_see()
    {
        var seed = Seed();
        await using var db = await NewDbAsync(seed.All);

        var result = await Handle(db, Restricted(), 14);

        Assert.True(result.IsSuccess);
        var summary = result.Value;
        Assert.Equal(2, CountOn(summary, new DateOnly(2026, 11, 21)));
        Assert.Equal(new PendingReviewSummary(1, 1, 3), summary.PendingReview);
        Assert.Equal(
            new[] { seed.Office.Id, seed.OfCustomerA.Id, seed.InReview.Id }.ToHashSet(),
            summary.Latest.Select(f => f.Id).ToHashSet()
        );
        Assert.False(summary.UploadsTruncated);
    }

    [Fact]
    public async Task Same_counts_with_view_all()
    {
        var seed = Seed();
        await using var db = await NewDbAsync(seed.All);

        var summary = (await Handle(db, new StorageActorScope(false, null) { TenantId = _tenant }, 14)).Value;

        Assert.Equal(3, CountOn(summary, new DateOnly(2026, 11, 21)));
        Assert.Contains(summary.Latest, f => f.Id == seed.OfCustomerB.Id);
        Assert.DoesNotContain(summary.Latest, f => f.Id == seed.Invoice.Id || f.Id == seed.Note.Id);
    }

    [Fact]
    public async Task Buckets_by_office_day()
    {
        var late = File(
            OwnerType.Tenant,
            null,
            FolderType.Documents,
            new DateTime(2026, 11, 21, 3, 0, 0, DateTimeKind.Utc)
        );
        await using var db = await NewDbAsync(late);

        var summary = (await Handle(db, Restricted(), 14)).Value;

        Assert.Equal("America/Chicago", summary.TimeZone);
        Assert.Equal(14, summary.UploadsByDay.Count);
        Assert.Equal(new DateOnly(2026, 11, 8), summary.UploadsByDay[0].Day);
        Assert.Equal(new DateOnly(2026, 11, 21), summary.UploadsByDay[^1].Day);
        Assert.Equal(1, CountOn(summary, new DateOnly(2026, 11, 20)));
        Assert.Equal(0, CountOn(summary, new DateOnly(2026, 11, 21)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public async Task Rejects_a_window_out_of_range(int days)
    {
        await using var db = await NewDbAsync();

        var result = await Handle(db, Restricted(), days);

        Assert.True(result.IsFailure);
        Assert.Equal("Storage.InvalidSummaryWindow", result.Error.Code);
    }

    [Fact]
    public async Task Marks_the_window_truncated_past_the_cap()
    {
        var stamps = Enumerable.Repeat(Now.AddHours(-1), FileSummaryLimits.MaxStamps + 1).ToList();

        var result = await GetFileSummaryHandler.Handle(
            new GetFileSummaryQuery(_tenant, Restricted(), 14),
            new FixedRows(new FileSummaryRows(stamps, 0, 0, [])),
            new FixedZone(Chicago),
            new FakeSystemClock(Now),
            CancellationToken.None
        );

        Assert.True(result.Value.UploadsTruncated);
        Assert.Equal(FileSummaryLimits.MaxStamps, result.Value.UploadsByDay.Sum(d => d.Count));
    }

    private StorageActorScope Restricted() =>
        new(false, null)
        {
            TenantId = _tenant,
            BypassAssignmentFilter = false,
            AssignedCustomerIds = [_customerA],
            AssignedUserId = _employee,
        };

    private static int CountOn(FileSummaryResponse summary, DateOnly day) =>
        summary.UploadsByDay.Single(d => d.Day == day).Count;

    private Task<BuildingBlocks.Results.Result<FileSummaryResponse>> Handle(
        CloudStorageDbContext db,
        StorageActorScope scope,
        int days
    ) =>
        GetFileSummaryHandler.Handle(
            new GetFileSummaryQuery(_tenant, scope, days),
            new FileSummaryReader(db),
            new FixedZone(Chicago),
            new FakeSystemClock(Now),
            CancellationToken.None
        );

    private sealed record Seeded(
        FileObject Office,
        FileObject OfCustomerA,
        FileObject OfCustomerB,
        FileObject Invoice,
        FileObject Note,
        FileObject Chat,
        FileObject InReview,
        FileObject NeverUploaded
    )
    {
        public FileObject[] All => [Office, OfCustomerA, OfCustomerB, Invoice, Note, Chat, InReview, NeverUploaded];
    }

    private Seeded Seed()
    {
        var today = Now.AddHours(-2);
        var inReview = File(OwnerType.Customer, _customerA, FolderType.Documents, Now.AddDays(-5), available: false);
        inReview.MarkPendingScan(Now.AddDays(-5));
        inReview.MarkScanning();
        inReview.MarkPendingReview("uncertain", "application/pdf", Checksum, Now.AddDays(-5));
        return new Seeded(
            File(OwnerType.Tenant, null, FolderType.Documents, today),
            File(OwnerType.Customer, _customerA, FolderType.Documents, today),
            File(OwnerType.Customer, _customerB, FolderType.Receipts, today),
            Declared(
                File(OwnerType.Invoice, Guid.NewGuid(), FolderType.Invoices, today),
                LibrarySourceCatalog.InvoiceResourceType,
                LibrarySourceCatalog.InvoicePdfRole,
                _customerA
            ),
            Declared(
                File(OwnerType.Customer, _customerA, FolderType.Other, today),
                FileVisibilityResourceTypes.Note,
                LibrarySourceCatalog.AttachmentRole,
                _customerA
            ),
            Declared(
                File(OwnerType.Communication, Guid.NewGuid(), FolderType.Other, today),
                FileVisibilityResourceTypes.Conversation,
                LibrarySourceCatalog.AttachmentRole,
                null
            ),
            inReview,
            File(OwnerType.Customer, _customerA, FolderType.Documents, today, available: false)
        );
    }

    private static readonly ChecksumSha256 Checksum = ChecksumSha256.Create(new string('a', 64)).Value;

    private FileObject File(
        OwnerType owner,
        Guid? ownerId,
        FolderType folder,
        DateTime createdAtUtc,
        bool available = true
    )
    {
        var id = Guid.NewGuid();
        var file = FileObject
            .Register(
                id,
                _tenant,
                owner,
                ownerId,
                folder,
                folder.RequiresYear() ? 2025 : null,
                ObjectKey.Create($"tenants/{_tenant:N}/{owner}/files/2025/{id:N}.pdf").Value,
                $"{id:N}.pdf",
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
        file.MarkAvailable(Checksum, "application/pdf", createdAtUtc);
        return file;
    }

    private static FileObject Declared(FileObject file, string resourceType, string role, Guid? relatedCustomerId)
    {
        file.DeclareSource(
            FileSourceDeclaration.Create("tests", resourceType, Guid.NewGuid(), role, relatedCustomerId).Value,
            Now
        );
        return file;
    }

    private async Task<CloudStorageDbContext> NewDbAsync(params FileObject[] files)
    {
        var db = new CloudStorageDbContext(
            new DbContextOptionsBuilder<CloudStorageDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FixedTenant(_tenant)
        );
        db.Files.AddRange(files);
        // El recorte cruza la proyección: el empleado tiene asignado solo al cliente A.
        db.CustomerAssignmentProjections.Add(CustomerAssignmentProjection.Create(_tenant, _customerA, _employee, Now));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private sealed class FixedZone(ResolvedTimeZone zone) : IOfficeTimeZone
    {
        public Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct) => Task.FromResult(zone);
    }

    private sealed class FixedRows(FileSummaryRows rows) : IFileSummaryReader
    {
        public Task<FileSummaryRows> ReadAsync(FileSummaryFilter filter, CancellationToken ct) => Task.FromResult(rows);
    }
}
