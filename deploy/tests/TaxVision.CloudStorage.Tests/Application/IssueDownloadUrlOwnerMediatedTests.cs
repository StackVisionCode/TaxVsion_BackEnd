using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Application.Sharing;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Sharing;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>Un empleado no baja ni comparte por id lo que solo media su dueño; un servicio sí lo baja.</summary>
public sealed class IssueDownloadUrlOwnerMediatedTests
{
    [Theory]
    [InlineData(OwnerType.Communication, FolderType.Other)]
    [InlineData(OwnerType.Communication, FolderType.Recordings)]
    [InlineData(OwnerType.Communication, FolderType.Transcripts)]
    [InlineData(OwnerType.Tenant, FolderType.VoiceNotes)]
    public async Task An_employee_with_download_permission_gets_not_found(OwnerType ownerType, FolderType folderType)
    {
        var file = OwnerMediatedFiles.Create(ownerType, folderType, available: true);

        var result = await IssueUrl(file, Employee());

        Assert.Equal(FileErrors.NotFound, result.Error);
    }

    [Theory]
    [InlineData(OwnerType.Communication, FolderType.Other)]
    [InlineData(OwnerType.Communication, FolderType.Recordings)]
    public async Task A_service_token_gets_the_url(OwnerType ownerType, FolderType folderType)
    {
        var file = OwnerMediatedFiles.Create(ownerType, folderType, available: true);
        var service = new StorageActorScope(false, null) { IsService = true };

        var result = await IssueUrl(file, service);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task An_employee_cannot_share_a_chat_attachment()
    {
        var file = OwnerMediatedFiles.Create(OwnerType.Communication, FolderType.Other, available: true);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var shares = new FakeShareLinkRepository();

        var result = await CreateShareLinkHandler.Handle(
            new CreateShareLinkCommand(
                file.TenantId,
                Guid.NewGuid(),
                Employee(),
                true,
                file.Id,
                ShareVisibility.TenantOnly,
                SharePermission.View,
                null,
                null,
                null,
                [],
                [],
                [],
                Audit()
            ),
            files,
            shares,
            new FakeStorageLimitRepository(),
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(FileErrors.NotFound, result.Error);
    }

    private static StorageActorScope Employee() =>
        new(false, null) { ReadableSurfaces = new HashSet<FileReadSurface>(), HidesOwnerMediated = true };

    private static RequestAuditContext Audit() => new(null, null, Guid.NewGuid().ToString());

    private static Task<Result<DownloadUrlResponse>> IssueUrl(FileObject file, StorageActorScope scope)
    {
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        return IssueDownloadUrlHandler.Handle(
            new IssueDownloadUrlQuery(file.TenantId, Guid.NewGuid(), scope, file.Id, Audit()),
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
