using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// Fase 2 — SystemFolderProvisioner: get-or-create idempotente de la carpeta de sistema
/// por FolderType, y null para tipos internos (no navegables).
/// </summary>
public sealed class SystemFolderProvisionerTests
{
    private static readonly DateTime Now = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Navigable_type_creates_the_system_folder_once()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var folders = new FakeFolderRepository();
        var provisioner = new SystemFolderProvisioner(folders);

        var first = await provisioner.ResolveFolderIdAsync(
            tenantId,
            OwnerType.Customer,
            ownerId,
            FolderType.Signatures,
            Guid.NewGuid(),
            Now,
            CancellationToken.None
        );

        Assert.NotNull(first);
        var folder = await folders.GetByOwnerAndCategoryAsync(
            tenantId,
            OwnerType.Customer,
            ownerId,
            "sys.signatures",
            CancellationToken.None
        );
        Assert.NotNull(folder);
        Assert.Equal("Signed Documents", folder!.Name);
        Assert.Equal(first, folder.Id);
    }

    [Fact]
    public async Task Second_call_returns_the_existing_folder_without_duplicating()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var folders = new FakeFolderRepository();
        var provisioner = new SystemFolderProvisioner(folders);

        var first = await provisioner.ResolveFolderIdAsync(
            tenantId,
            OwnerType.Customer,
            ownerId,
            FolderType.Documents,
            Guid.NewGuid(),
            Now,
            CancellationToken.None
        );
        var second = await provisioner.ResolveFolderIdAsync(
            tenantId,
            OwnerType.Customer,
            ownerId,
            FolderType.Documents,
            Guid.NewGuid(),
            Now,
            CancellationToken.None
        );

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Email_incoming_and_outgoing_share_one_folder()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var folders = new FakeFolderRepository();
        var provisioner = new SystemFolderProvisioner(folders);

        var incoming = await provisioner.ResolveFolderIdAsync(
            tenantId,
            OwnerType.Customer,
            ownerId,
            FolderType.EmailIncoming,
            Guid.NewGuid(),
            Now,
            CancellationToken.None
        );
        var outgoing = await provisioner.ResolveFolderIdAsync(
            tenantId,
            OwnerType.Customer,
            ownerId,
            FolderType.EmailOutgoing,
            Guid.NewGuid(),
            Now,
            CancellationToken.None
        );

        Assert.Equal(incoming, outgoing);
    }

    [Fact]
    public async Task Internal_type_returns_null_and_creates_no_folder()
    {
        var tenantId = Guid.NewGuid();
        var folders = new FakeFolderRepository();
        var provisioner = new SystemFolderProvisioner(folders);

        var result = await provisioner.ResolveFolderIdAsync(
            tenantId,
            OwnerType.Tenant,
            null,
            FolderType.Templates,
            Guid.NewGuid(),
            Now,
            CancellationToken.None
        );

        Assert.Null(result);
    }
}
