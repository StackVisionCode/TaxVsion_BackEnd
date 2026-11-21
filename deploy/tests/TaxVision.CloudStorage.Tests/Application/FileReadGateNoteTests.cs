using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// Un adjunto declarado por Notes solo lo entrega Notes: para el personal y el portal no existe por id; un servicio lo
/// sigue bajando. Un archivo de cliente en Other sin declarar no cambia.
/// </summary>
public sealed class FileReadGateNoteTests
{
    [Theory]
    [InlineData(OwnerType.Customer)]
    [InlineData(OwnerType.Tenant)]
    public void A_declared_note_attachment_is_owner_mediated(OwnerType owner)
    {
        Assert.True(FileReadGate.IsOwnerMediated(NoteFile(owner)));
    }

    [Fact]
    public void An_undeclared_file_in_other_is_not_owner_mediated()
    {
        Assert.False(FileReadGate.IsOwnerMediated(OwnerMediatedFiles.Create(OwnerType.Customer, FolderType.Other)));
    }

    [Fact]
    public void A_file_declared_by_another_service_is_not_owner_mediated_by_this_rule()
    {
        var file = OwnerMediatedFiles.Create(OwnerType.Customer, FolderType.Tasks);
        Declare(file, "tasks", "task");

        Assert.False(FileReadGate.IsOwnerMediated(file));
    }

    [Fact]
    public async Task An_employee_with_download_permission_gets_not_found()
    {
        var result = await IssueUrl(NoteFile(OwnerType.Customer), Employee());

        Assert.Equal(FileErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task The_customer_of_the_note_gets_not_found_in_the_portal()
    {
        var file = NoteFile(OwnerType.Customer);
        var portal = new StorageActorScope(true, file.OwnerId) { HidesOwnerMediated = true };

        var result = await IssueUrl(file, portal);

        Assert.Equal(FileErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task A_service_token_gets_the_url()
    {
        var result = await IssueUrl(
            NoteFile(OwnerType.Customer),
            new StorageActorScope(false, null) { IsService = true }
        );

        Assert.True(result.IsSuccess);
    }

    internal static FileObject NoteFile(OwnerType owner, DateTime? createdAtUtc = null)
    {
        var file = OwnerMediatedFiles.Create(owner, FolderType.Other, available: true, createdAtUtc: createdAtUtc);
        Declare(file, "notes", "note");
        return file;
    }

    private static void Declare(FileObject file, string service, string resourceType) =>
        Assert.True(
            file.DeclareSource(
                FileSourceDeclaration.Create(service, resourceType, Guid.NewGuid(), "attachment", null).Value,
                DateTime.UtcNow
            ).IsSuccess
        );

    private static StorageActorScope Employee() =>
        new(false, null) { ReadableSurfaces = new HashSet<FileReadSurface>(), HidesOwnerMediated = true };

    private static Task<Result<DownloadUrlResponse>> IssueUrl(FileObject file, StorageActorScope scope)
    {
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        return IssueDownloadUrlHandler.Handle(
            new IssueDownloadUrlQuery(
                file.TenantId,
                Guid.NewGuid(),
                scope,
                file.Id,
                new RequestAuditContext(null, null, Guid.NewGuid().ToString())
            ),
            files,
            new FakeStorageAuditRepository(),
            new FakeObjectStorage(),
            Options.Create(new CloudStorageOptions()),
            new FakeStoragePlatformSettingsProvider(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );
    }
}
