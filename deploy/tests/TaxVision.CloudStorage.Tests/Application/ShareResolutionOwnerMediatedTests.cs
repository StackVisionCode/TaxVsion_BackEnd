using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Sharing;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Sharing;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>Un enlace que ya existe nunca entrega un archivo que solo media su dueño.</summary>
public sealed class ShareResolutionOwnerMediatedTests
{
    private static readonly Guid Tenant = OwnerMediatedFiles.Tenant;
    private static readonly RequestAuditContext Audit = new(null, null, "corr-1");

    [Theory]
    [InlineData(OwnerType.Communication, FolderType.Other)]
    [InlineData(OwnerType.Communication, FolderType.Recordings)]
    [InlineData(OwnerType.Tenant, FolderType.VoiceNotes)]
    public async Task A_file_link_answers_as_unavailable(OwnerType ownerType, FolderType folderType)
    {
        var file = OwnerMediatedFiles.Create(ownerType, folderType, available: true);
        var (shares, token) = Link(file.Id, ShareResourceType.File);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var access = await ResolvePublicShareHandler.Handle(
            new ResolvePublicShareQuery(token, null, null, null, null, null),
            shares,
            files,
            new FakeFolderRepository(),
            new FakeObjectStorage(),
            new FakeShareLinkPasswordHasher(),
            new FakeStorageLimitRepository(),
            new FakeStoragePlatformSettingsProvider(),
            new FakeStorageAuditRepository(),
            Options.Create(new CloudStorageOptions()),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );
        var meta = await ResolvePublicShareMetaHandler.Handle(
            new ResolvePublicShareMetaQuery(token, null, null),
            shares,
            files,
            new FakeFolderRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            CancellationToken.None
        );

        Assert.Equal(ShareAccessOutcome.Denied, access.Outcome);
        Assert.Equal(ShareMetaOutcome.NotFound, meta.Outcome);
    }

    [Fact]
    public async Task A_folder_link_skips_them_when_listing_and_zipping()
    {
        var folder = OwnerMediatedFiles.SystemFolder("Shared", "custom.shared");
        var document = InFolder(
            OwnerMediatedFiles.Create(OwnerType.Tenant, FolderType.Documents, available: true),
            folder
        );
        var note = InFolder(
            OwnerMediatedFiles.Create(OwnerType.Tenant, FolderType.VoiceNotes, available: true),
            folder
        );
        var (shares, token) = Link(folder.Id, ShareResourceType.Folder);
        var files = new FakeFileObjectRepository();
        files.Seed(document);
        files.Seed(note);
        var folders = new FakeFolderRepository();
        folders.Seed(folder);

        var contents = await Contents(token, shares, files, folders);
        var zip = await Zip(token, shares, files, folders);

        Assert.Equal(document.Id, Assert.Single(contents.Files!).FileId);
        Assert.Equal(document.Id, Assert.Single(zip.Plan!.Entries).FileId);
    }

    [Fact]
    public async Task A_link_on_the_voice_notes_folder_is_denied()
    {
        var folder = OwnerMediatedFiles.SystemFolder("Voice Notes", "sys.voicenotes");
        var note = InFolder(
            OwnerMediatedFiles.Create(OwnerType.Tenant, FolderType.VoiceNotes, available: true),
            folder
        );
        var (shares, token) = Link(folder.Id, ShareResourceType.Folder);
        var files = new FakeFileObjectRepository();
        files.Seed(note);
        var folders = new FakeFolderRepository();
        folders.Seed(folder);

        var contents = await Contents(token, shares, files, folders);
        var zip = await Zip(token, shares, files, folders);

        Assert.Equal(PublicFolderOutcome.Denied, contents.Outcome);
        Assert.Equal(PublicZipOutcome.Denied, zip.Outcome);
    }

    private static FileObject InFolder(FileObject file, Folder folder)
    {
        file.MoveToFolder(folder.Id, DateTime.UtcNow.AddMinutes(-10));
        return file;
    }

    private static (FakeShareLinkRepository Shares, string Token) Link(Guid resourceId, ShareResourceType type)
    {
        var created = ShareLink
            .Create(
                Guid.NewGuid(),
                Tenant,
                resourceId,
                type,
                ShareVisibility.ExternalLink,
                SharePermission.Download,
                null,
                null,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow,
                isRecursive: type == ShareResourceType.Folder,
                appliesToFutureItems: false
            )
            .Value;
        var shares = new FakeShareLinkRepository();
        shares.Seed(created.Link);
        return (shares, created.PlainToken);
    }

    private static Task<PublicFolderContentsResult> Contents(
        string token,
        FakeShareLinkRepository shares,
        FakeFileObjectRepository files,
        FakeFolderRepository folders
    ) =>
        ResolvePublicFolderContentsHandler.Handle(
            new ResolvePublicFolderContentsQuery(token, null, null, null),
            shares,
            files,
            folders,
            new FakeShareLinkPasswordHasher(),
            new FakeStorageLimitRepository(),
            new FakeStoragePlatformSettingsProvider(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

    private static Task<PublicZipResult> Zip(
        string token,
        FakeShareLinkRepository shares,
        FakeFileObjectRepository files,
        FakeFolderRepository folders
    ) =>
        PreparePublicFolderZipHandler.Handle(
            new PreparePublicFolderZipQuery(token, null, null, null, Audit),
            shares,
            files,
            folders,
            new FakeShareLinkPasswordHasher(),
            new FakeStorageLimitRepository(),
            new FakeStoragePlatformSettingsProvider(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            CancellationToken.None
        );
}
