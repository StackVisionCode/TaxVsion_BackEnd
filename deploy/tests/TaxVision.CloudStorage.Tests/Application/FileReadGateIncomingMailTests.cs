using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// Un adjunto entrante sin cliente (User/EmailIncoming) solo lo entrega Correspondence: por id no existe para una
/// persona. Lo de la carpeta "Email" del cliente y lo enviado sin cliente no cambian (E-5).
/// </summary>
public sealed class FileReadGateIncomingMailTests
{
    [Fact]
    public void An_incoming_attachment_without_customer_is_owner_mediated()
    {
        Assert.True(FileReadGate.IsOwnerMediated(OwnerMediatedFiles.Create(OwnerType.User, FolderType.EmailIncoming)));
    }

    [Theory]
    [InlineData(OwnerType.Customer, FolderType.EmailIncoming)]
    [InlineData(OwnerType.Customer, FolderType.EmailOutgoing)]
    [InlineData(OwnerType.User, FolderType.EmailOutgoing)]
    public void Customer_email_folder_and_sent_without_customer_stay_open(OwnerType owner, FolderType folder)
    {
        Assert.False(FileReadGate.IsOwnerMediated(OwnerMediatedFiles.Create(owner, folder)));
    }

    [Fact]
    public async Task An_employee_with_download_permission_gets_not_found()
    {
        var file = OwnerMediatedFiles.Create(OwnerType.User, FolderType.EmailIncoming, available: true);

        var result = await IssueUrl(
            file,
            new StorageActorScope(false, null)
            {
                ReadableSurfaces = new HashSet<FileReadSurface>(),
                HidesOwnerMediated = true,
            }
        );

        Assert.Equal(FileErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Correspondence_with_a_service_token_still_gets_the_url()
    {
        var file = OwnerMediatedFiles.Create(OwnerType.User, FolderType.EmailIncoming, available: true);

        var result = await IssueUrl(file, new StorageActorScope(false, null) { IsService = true });

        Assert.True(result.IsSuccess);
    }

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
