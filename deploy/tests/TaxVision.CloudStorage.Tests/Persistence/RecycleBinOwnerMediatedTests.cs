using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Files.RecycleBin;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using TaxVision.CloudStorage.Tests.Application;

namespace TaxVision.CloudStorage.Tests.Persistence;

/// <summary>La papelera de una persona no muestra ni restaura lo que solo media su dueño.</summary>
public sealed class RecycleBinOwnerMediatedTests
{
    private static readonly Guid Tenant = OwnerMediatedFiles.Tenant;

    private sealed class TenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid tenantId) { }
    }

    [Fact]
    public async Task A_person_does_not_see_owner_mediated_files_in_the_bin()
    {
        var (document, mediated) = DeletedFiles();
        await using var db = await SeedAsync([document, .. mediated]);

        var bin = await Bin(db, Person());

        Assert.Equal(document.Id, Assert.Single(bin).Id);
    }

    [Fact]
    public async Task A_service_scope_still_sees_them()
    {
        var (document, mediated) = DeletedFiles();
        await using var db = await SeedAsync([document, .. mediated]);

        var bin = await Bin(db, new StorageActorScope(false, null) { IsService = true });

        Assert.Equal(mediated.Length + 1, bin.Count);
    }

    [Theory]
    [InlineData(OwnerType.Communication, FolderType.Other)]
    [InlineData(OwnerType.Tenant, FolderType.VoiceNotes)]
    public async Task A_person_cannot_restore_one_by_id(OwnerType ownerType, FolderType folderType)
    {
        var file = Deleted(OwnerMediatedFiles.Create(ownerType, folderType, available: true));
        await using var db = await SeedAsync([file]);

        var result = await RestoreFileHandler.Handle(
            new RestoreFileCommand(Tenant, Guid.NewGuid(), Person(), file.Id, Audit()),
            new FileObjectRepository(db),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.Equal(FileErrors.NotFound, result.Error);
    }

    private static StorageActorScope Person() => new(false, null) { TenantId = Tenant, HidesOwnerMediated = true };

    private static RequestAuditContext Audit() => new(null, null, Guid.NewGuid().ToString());

    private static (FileObject Document, FileObject[] Mediated) DeletedFiles()
    {
        var now = DateTime.UtcNow;
        var document = Deleted(OwnerMediatedFiles.Create(OwnerType.Customer, FolderType.Documents, available: true));
        FileObject[] mediated =
        [
            Deleted(OwnerMediatedFiles.Create(OwnerType.Communication, FolderType.Other, available: true)),
            Deleted(OwnerMediatedFiles.Create(OwnerType.Communication, FolderType.Transcripts, available: true)),
            Deleted(OwnerMediatedFiles.Create(OwnerType.Tenant, FolderType.VoiceNotes, available: true)),
        ];
        return (document, mediated);
    }

    private static FileObject Deleted(FileObject file)
    {
        Assert.True(file.SoftDelete(DateTime.UtcNow, TimeSpan.FromDays(30)).IsSuccess);
        return file;
    }

    private static Task<IReadOnlyList<RecycleBinItemResponse>> Bin(CloudStorageDbContext db, StorageActorScope scope) =>
        GetRecycleBinHandler.Handle(
            new GetRecycleBinQuery(Tenant, scope, 0, 50),
            new FileObjectRepository(db),
            new FolderRepository(db),
            CancellationToken.None
        );

    private static async Task<CloudStorageDbContext> SeedAsync(FileObject[] files)
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
