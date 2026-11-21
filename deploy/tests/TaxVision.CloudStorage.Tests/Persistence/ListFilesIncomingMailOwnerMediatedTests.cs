using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using TaxVision.CloudStorage.Tests.Application;

namespace TaxVision.CloudStorage.Tests.Persistence;

/// <summary>El personal no lista los adjuntos entrantes sin cliente; los del cliente y los enviados siguen saliendo.</summary>
public sealed class ListFilesIncomingMailOwnerMediatedTests
{
    private static readonly Guid Tenant = OwnerMediatedFiles.Tenant;

    private sealed class TenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid tenantId) { }
    }

    [Fact]
    public async Task Staff_does_not_list_incoming_attachments_without_customer()
    {
        var start = DateTime.UtcNow;
        var userIncoming = OwnerMediatedFiles.Create(OwnerType.User, FolderType.EmailIncoming, true, null, start);
        var customerIncoming = OwnerMediatedFiles.Create(
            OwnerType.Customer,
            FolderType.EmailIncoming,
            true,
            null,
            start.AddMinutes(1)
        );
        var userOutgoing = OwnerMediatedFiles.Create(
            OwnerType.User,
            FolderType.EmailOutgoing,
            true,
            null,
            start.AddMinutes(2)
        );
        await using var db = await NewDbAsync(userIncoming, customerIncoming, userOutgoing);

        var staff = await List(db, new StorageActorScope(false, null) { TenantId = Tenant, HidesOwnerMediated = true });
        var service = await List(db, new StorageActorScope(false, null) { IsService = true });

        Assert.DoesNotContain(staff, f => f.Id == userIncoming.Id);
        Assert.Contains(staff, f => f.Id == customerIncoming.Id);
        Assert.Contains(staff, f => f.Id == userOutgoing.Id);
        Assert.Contains(service, f => f.Id == userIncoming.Id);
    }

    private static Task<IReadOnlyList<FileResponse>> List(CloudStorageDbContext db, StorageActorScope scope) =>
        ListFilesHandler.Handle(
            new ListFilesQuery(Tenant, scope, null, null, 0, 50),
            new FileObjectRepository(db),
            CancellationToken.None
        );

    private static async Task<CloudStorageDbContext> NewDbAsync(params FileObject[] files)
    {
        var db = new CloudStorageDbContext(
            new DbContextOptionsBuilder<CloudStorageDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new TenantContext(Tenant)
        );
        db.Files.AddRange(files);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }
}
