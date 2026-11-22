using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>
/// El recorte del resumen Documents como EXISTS contra la proyección por usuario da lo mismo que la lista de ids del
/// resolver (la regla anterior, copiada aquí), contra SQL Server: archivos de la oficina, de clientes asignados y sin
/// asignar, en revisión, fuera de la rama Documents y de otra oficina con la misma asignación.
/// </summary>
public sealed class FileSummaryVisibilityEquivalenceIntegrationTests : IAsyncLifetime
{
    private static readonly ChecksumSha256 Checksum = ChecksumSha256.Create(new string('a', 64)).Value;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _otherTenant = Guid.NewGuid();
    private readonly Guid _employee = Guid.NewGuid();
    private readonly Guid _colleague = Guid.NewGuid();
    private readonly Guid _assignedA = Guid.NewGuid();
    private readonly Guid _assignedB = Guid.NewGuid();
    private readonly Guid _unassigned = Guid.NewGuid();
    private readonly DateTime _now = DateTime.UtcNow;

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        db.Files.AddRange(
            File(_tenant, OwnerType.Tenant, null, FolderType.Documents, _now.AddHours(-1)),
            File(_tenant, OwnerType.Customer, _assignedA, FolderType.Documents, _now.AddHours(-2)),
            File(_tenant, OwnerType.Customer, _assignedA, FolderType.Receipts, _now.AddDays(-2)),
            File(_tenant, OwnerType.Customer, _assignedB, FolderType.Invoices, _now.AddHours(-3)),
            File(_tenant, OwnerType.Customer, _unassigned, FolderType.Documents, _now.AddMinutes(-5)),
            File(_tenant, OwnerType.Customer, _unassigned, FolderType.Documents, _now.AddDays(-20)),
            File(_tenant, OwnerType.Customer, _assignedA, FolderType.Other, _now.AddMinutes(-2)),
            InReview(_tenant, OwnerType.Customer, _assignedA, _now.AddDays(-5)),
            InReview(_tenant, OwnerType.Customer, _unassigned, _now.AddDays(-6)),
            InReview(_tenant, OwnerType.Tenant, null, _now.AddDays(-1)),
            File(_tenant, OwnerType.Customer, _assignedB, FolderType.Documents, _now.AddMinutes(-3), available: false),
            File(_otherTenant, OwnerType.Customer, _unassigned, FolderType.Documents, _now.AddMinutes(-4))
        );
        db.CustomerAssignmentProjections.AddRange(
            CustomerAssignmentProjection.Create(_tenant, _assignedA, _employee, _now),
            CustomerAssignmentProjection.Create(_tenant, _assignedB, _employee, _now),
            CustomerAssignmentProjection.Create(_tenant, _unassigned, _colleague, _now),
            CustomerAssignmentProjection.Create(_otherTenant, _unassigned, _employee, _now)
        );
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db
            .Files.IgnoreQueryFilters()
            .Where(f => f.TenantId == _tenant || f.TenantId == _otherTenant)
            .ExecuteDeleteAsync();
        await db
            .CustomerAssignmentProjections.IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenant || a.TenantId == _otherTenant)
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Every_viewer_gets_the_same_summary_as_the_id_list()
    {
        foreach (var viewer in new Guid?[] { _employee, _colleague, Guid.NewGuid(), null })
        {
            var current = await ReadAsync(viewer);
            var legacy = await LegacyReadAsync(viewer);

            Assert.Equal(legacy.CreatedInWindow, current.CreatedInWindow);
            Assert.Equal(legacy.PendingReview, current.PendingReview);
            Assert.Equal(legacy.StalePendingReview, current.StalePendingReview);
            Assert.Equal(legacy.Latest, current.Latest);
        }
    }

    [Fact]
    public async Task The_employee_sees_the_office_and_his_clients_only()
    {
        var summary = await ReadAsync(_employee);

        Assert.Equal(6, summary.CreatedInWindow.Count);
        Assert.Equal((2, 1), (summary.PendingReview, summary.StalePendingReview));
    }

    private async Task<FileSummaryRows> ReadAsync(Guid? viewer)
    {
        await using var db = NewContext();
        return await new FileSummaryReader(db).ReadAsync(Filter(viewer), CancellationToken.None);
    }

    private FileSummaryFilter Filter(Guid? viewer) =>
        new(_tenant, viewer, _now.AddDays(-14), _now.AddDays(-3), 20_000, 3);

    // La regla anterior: el resolver lee los ids de la proyección y el lector filtra con la lista.
    private async Task<FileSummaryRows> LegacyReadAsync(Guid? viewer)
    {
        await using var db = NewContext();
        var filter = Filter(viewer);
        List<Guid>? assigned = viewer is { } user
            ? await db
                .CustomerAssignmentProjections.IgnoreQueryFilters()
                .Where(a => a.TenantId == _tenant && a.UserId == user)
                .Select(a => a.CustomerId)
                .ToListAsync()
            : null;
        var restrict = assigned is not null;
        var ids = assigned ?? [];
        var documents = db
            .Files.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(file =>
                file.TenantId == _tenant
                && file.SourceResourceType == null
                && (file.OwnerType == OwnerType.Customer || file.OwnerType == OwnerType.Tenant)
                && (
                    file.FolderType == FolderType.Documents
                    || file.FolderType == FolderType.Receipts
                    || file.FolderType == FolderType.Invoices
                )
                && (
                    !restrict
                    || file.OwnerType == OwnerType.Tenant
                    || (file.OwnerId != null && ids.Contains(file.OwnerId.Value))
                )
            );
        var uploads = documents.Where(file =>
            file.Status != FileStatus.PendingUpload && file.Status != FileStatus.SoftDeleted
        );
        var stamps = await uploads
            .Where(file => file.CreatedAtUtc >= filter.FromUtc)
            .OrderByDescending(file => file.CreatedAtUtc)
            .Select(file => file.CreatedAtUtc)
            .Take(filter.MaxStamps + 1)
            .ToListAsync();
        var inReview = documents.Where(file => file.Status == FileStatus.PendingReview);
        var pending = await inReview.CountAsync();
        var stale = await inReview.CountAsync(file => file.CreatedAtUtc < filter.StaleBeforeUtc);
        var latest = await uploads
            .OrderByDescending(file => file.CreatedAtUtc)
            .ThenByDescending(file => file.Id)
            .Take(filter.Latest)
            .Select(file => new FileSummaryItem(
                file.Id,
                file.OriginalName,
                file.OwnerType,
                file.Status,
                file.CreatedAtUtc
            ))
            .ToListAsync();
        return new FileSummaryRows(stamps, pending, stale, latest);
    }

    private static FileObject InReview(Guid tenant, OwnerType owner, Guid? ownerId, DateTime createdAtUtc)
    {
        var file = File(tenant, owner, ownerId, FolderType.Documents, createdAtUtc, available: false);
        file.MarkPendingScan(createdAtUtc);
        file.MarkScanning();
        file.MarkPendingReview("uncertain", "application/pdf", Checksum, createdAtUtc);
        return file;
    }

    private static FileObject File(
        Guid tenant,
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
                tenant,
                owner,
                ownerId,
                folder,
                folder.RequiresYear() ? 2025 : null,
                ObjectKey.Create($"tenants/{tenant:N}/{owner}/files/2025/{id:N}.pdf").Value,
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

    private static CloudStorageDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CloudStorageDbContext>()
                .UseSqlServer(CloudStorageIntegrationDatabase.ConnectionString)
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
