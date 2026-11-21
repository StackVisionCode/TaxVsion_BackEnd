using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Application.Files.RecycleBin;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using TaxVision.CloudStorage.Tests.Application;

namespace TaxVision.CloudStorage.Tests.Persistence;

/// <summary>
/// Los adjuntos declarados de notas no salen en los listados del personal ni en "My documents" del portal, ni en la
/// papelera; un servicio sí los ve. Un archivo de cliente en Other sin declarar sigue saliendo.
/// </summary>
public sealed class ListFilesNoteOwnerMediatedTests
{
    private static readonly Guid Tenant = OwnerMediatedFiles.Tenant;

    private sealed class TenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid tenantId) { }
    }

    [Fact]
    public async Task Staff_does_not_list_declared_note_attachments()
    {
        var (db, undeclared, note) = await SeedAsync();
        await using var _ = db;

        var listed = await List(
            db,
            new StorageActorScope(false, null) { TenantId = Tenant, HidesOwnerMediated = true }
        );

        Assert.Contains(listed, f => f.Id == undeclared.Id);
        Assert.DoesNotContain(listed, f => f.Id == note.Id);
    }

    [Fact]
    public async Task The_portal_my_documents_list_does_not_bring_them()
    {
        var (db, _, note) = await SeedAsync();
        await using var _db = db;
        var portal = new StorageActorScope(true, note.OwnerId) { TenantId = Tenant, HidesOwnerMediated = true };

        var listed = await List(db, portal);

        Assert.DoesNotContain(listed, f => f.Id == note.Id);
    }

    [Fact]
    public async Task A_service_still_lists_them()
    {
        var (db, _, note) = await SeedAsync();
        await using var _db = db;

        var listed = await List(db, new StorageActorScope(false, null) { IsService = true });

        Assert.Contains(listed, f => f.Id == note.Id);
    }

    [Fact]
    public async Task Staff_does_not_see_them_in_the_recycle_bin()
    {
        var note = FileReadGateNoteTests.NoteFile(OwnerType.Customer);
        Assert.True(note.SoftDelete(DateTime.UtcNow, TimeSpan.FromDays(30)).IsSuccess);
        await using var db = await NewDbAsync(note);

        var bin = await GetRecycleBinHandler.Handle(
            new GetRecycleBinQuery(
                Tenant,
                new StorageActorScope(false, null) { TenantId = Tenant, HidesOwnerMediated = true },
                0,
                50
            ),
            new FileObjectRepository(db),
            new FolderRepository(db),
            CancellationToken.None
        );

        Assert.Empty(bin);
    }

    private static async Task<(CloudStorageDbContext Db, FileObject Undeclared, FileObject Note)> SeedAsync()
    {
        var start = DateTime.UtcNow;
        var undeclared = OwnerMediatedFiles.Create(
            OwnerType.Customer,
            FolderType.Other,
            available: true,
            createdAtUtc: start
        );
        var note = FileReadGateNoteTests.NoteFile(OwnerType.Customer, start.AddMinutes(1));
        return (await NewDbAsync(undeclared, note), undeclared, note);
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
