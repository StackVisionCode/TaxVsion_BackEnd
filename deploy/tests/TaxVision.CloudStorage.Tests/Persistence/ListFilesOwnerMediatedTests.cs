using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using TaxVision.CloudStorage.Tests.Application;

namespace TaxVision.CloudStorage.Tests.Persistence;

/// <summary>Los listados de una persona no traen archivos de Communication ni notas de voz; los de un servicio sí.</summary>
public sealed class ListFilesOwnerMediatedTests
{
    private static readonly Guid Tenant = OwnerMediatedFiles.Tenant;

    private sealed class TenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid tenantId) { }
    }

    [Fact]
    public async Task A_person_lists_no_communication_files_nor_voice_notes()
    {
        var (db, document, mediated) = await SeedFlatAsync();
        await using var _ = db;

        var listed = await List(db, Person());

        Assert.Equal(document.Id, Assert.Single(listed).Id);
        Assert.DoesNotContain(listed, f => mediated.Contains(f.Id));
    }

    [Fact]
    public async Task A_person_filtering_by_communication_gets_an_empty_list()
    {
        var (db, _, _) = await SeedFlatAsync();
        await using var __ = db;

        var listed = await List(db, Person(), OwnerType.Communication);

        Assert.Empty(listed);
    }

    [Fact]
    public async Task A_service_still_lists_them()
    {
        var (db, document, mediated) = await SeedFlatAsync();
        await using var _ = db;

        var listed = await List(db, new StorageActorScope(false, null) { IsService = true });

        Assert.Equal(mediated.Append(document.Id).Order(), listed.Select(f => f.Id).Order());
    }

    [Fact]
    public async Task A_person_does_not_see_the_voice_notes_folder_nor_its_files()
    {
        var voiceNotes = OwnerMediatedFiles.SystemFolder("Voice Notes", "sys.voicenotes");
        var documents = OwnerMediatedFiles.SystemFolder("Documents", "sys.documents");
        var note = OwnerMediatedFiles.Create(OwnerType.Tenant, FolderType.VoiceNotes, folderId: voiceNotes.Id);
        await using var db = await SeedAsync([voiceNotes, documents], note);

        var root = await Contents(db, Person(), null);
        var inside = await Contents(db, Person(), voiceNotes.Id);
        var tree = await Tree(db, Person());

        Assert.Equal(documents.Id, Assert.Single(root.Subfolders).Id);
        Assert.Equal(1, root.FolderCount);
        Assert.Empty(inside.Files);
        Assert.Equal(0, inside.FileCount);
        Assert.Equal(documents.Id, Assert.Single(tree.Value).Id);
    }

    [Fact]
    public async Task A_service_still_sees_the_voice_notes_folder()
    {
        var voiceNotes = OwnerMediatedFiles.SystemFolder("Voice Notes", "sys.voicenotes");
        var note = OwnerMediatedFiles.Create(OwnerType.Tenant, FolderType.VoiceNotes, folderId: voiceNotes.Id);
        await using var db = await SeedAsync([voiceNotes], note);
        var service = new StorageActorScope(false, null) { IsService = true };

        var root = await Contents(db, service, null);
        var inside = await Contents(db, service, voiceNotes.Id);

        Assert.Equal(voiceNotes.Id, Assert.Single(root.Subfolders).Id);
        Assert.Equal(note.Id, Assert.Single(inside.Files).Id);
    }

    [Fact]
    public async Task A_person_cannot_zip_the_voice_notes_folder()
    {
        var voiceNotes = OwnerMediatedFiles.SystemFolder("Voice Notes", "sys.voicenotes");
        var note = OwnerMediatedFiles.Create(
            OwnerType.Tenant,
            FolderType.VoiceNotes,
            available: true,
            folderId: voiceNotes.Id
        );
        await using var db = await SeedAsync([voiceNotes], note);

        var plan = await PrepareZipDownloadHandler.Handle(
            new PrepareZipDownloadQuery(
                Tenant,
                Guid.NewGuid(),
                Person(),
                [],
                [voiceNotes.Id],
                new RequestAuditContext(null, null, Guid.NewGuid().ToString())
            ),
            new FileObjectRepository(db),
            new FolderRepository(db),
            new FakeStorageAuditRepository(),
            new FakeStoragePlatformSettingsProvider(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.Equal(FolderErrors.NotFound, plan.Error);
    }

    private static StorageActorScope Person() => new(false, null) { TenantId = Tenant, HidesOwnerMediated = true };

    private static async Task<(CloudStorageDbContext Db, FileObject Document, Guid[] Mediated)> SeedFlatAsync()
    {
        var start = DateTime.UtcNow;
        var document = OwnerMediatedFiles.Create(OwnerType.Customer, FolderType.Documents, createdAtUtc: start);
        FileObject[] mediated =
        [
            OwnerMediatedFiles.Create(OwnerType.Communication, FolderType.Other, createdAtUtc: start.AddMinutes(1)),
            OwnerMediatedFiles.Create(
                OwnerType.Communication,
                FolderType.Recordings,
                createdAtUtc: start.AddMinutes(2)
            ),
            OwnerMediatedFiles.Create(
                OwnerType.Communication,
                FolderType.Transcripts,
                createdAtUtc: start.AddMinutes(3)
            ),
            OwnerMediatedFiles.Create(OwnerType.Tenant, FolderType.VoiceNotes, createdAtUtc: start.AddMinutes(4)),
        ];
        var db = await SeedAsync([], [document, .. mediated]);
        return (db, document, mediated.Select(f => f.Id).ToArray());
    }

    private static Task<IReadOnlyList<FileResponse>> List(
        CloudStorageDbContext db,
        StorageActorScope scope,
        OwnerType? ownerType = null
    ) =>
        ListFilesHandler.Handle(
            new ListFilesQuery(Tenant, scope, ownerType, null, 0, 50),
            new FileObjectRepository(db),
            CancellationToken.None
        );

    private static Task<FolderContentsResponse> Contents(
        CloudStorageDbContext db,
        StorageActorScope scope,
        Guid? folderId
    ) =>
        GetFolderContentsHandler.Handle(
            new GetFolderContentsQuery(Tenant, scope, folderId, OwnerType.Tenant),
            new FolderRepository(db),
            new FileObjectRepository(db),
            new FakeShareLinkRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            CancellationToken.None
        );

    private static Task<BuildingBlocks.Results.Result<IReadOnlyList<FolderTreeNode>>> Tree(
        CloudStorageDbContext db,
        StorageActorScope scope
    ) =>
        GetFolderTreeHandler.Handle(
            new GetFolderTreeQuery(Tenant, scope, OwnerType.Tenant),
            new FolderRepository(db),
            CancellationToken.None
        );

    private static async Task<CloudStorageDbContext> SeedAsync(Folder[] folders, params FileObject[] files)
    {
        var db = new CloudStorageDbContext(
            new DbContextOptionsBuilder<CloudStorageDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new TenantContext(Tenant)
        );
        db.Folders.AddRange(folders);
        db.Files.AddRange(files);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }
}
