using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Infrastructure.Storage;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>La copia por cliente de un documento firmado nace declarada cuando la pide un servicio.</summary>
public sealed class CopyFileSourceTests
{
    [Fact]
    public async Task A_service_copy_with_a_source_is_declared_for_the_destination_customer()
    {
        var requestId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var copy = await CopyAsync(
            isService: true,
            customerId,
            new FileSourceDeclarationRequest("signature_request", requestId, "sealed")
        );

        Assert.Equal("signature", copy.SourceService);
        Assert.Equal("signature_request", copy.SourceResourceType);
        Assert.Equal(requestId, copy.SourceResourceId);
        Assert.Equal("sealed", copy.SourceRole);
        Assert.Equal(customerId, copy.RelatedCustomerId);
    }

    [Fact]
    public async Task A_person_cannot_declare_the_source_of_a_copy()
    {
        var copy = await CopyAsync(
            isService: false,
            Guid.NewGuid(),
            new FileSourceDeclarationRequest("signature_request", Guid.NewGuid(), "sealed")
        );

        Assert.Null(copy.SourceResourceType);
    }

    [Fact]
    public async Task A_source_that_does_not_fit_the_destination_is_ignored()
    {
        var copy = await CopyAsync(
            isService: true,
            Guid.NewGuid(),
            new FileSourceDeclarationRequest("invoice", Guid.NewGuid(), "invoice_pdf")
        );

        Assert.Null(copy.SourceResourceType);
    }

    private static async Task<FileObject> CopyAsync(
        bool isService,
        Guid customerId,
        FileSourceDeclarationRequest source
    )
    {
        var tenantId = Guid.NewGuid();
        var original = SignedOriginal(tenantId);
        var files = new FakeFileObjectRepository();
        files.Seed(original);
        var folders = new FakeFolderRepository();
        var limits = new FakeStorageLimitRepository();
        limits.Seed(TenantStorageLimit.Create(tenantId, "starter", 100_000_000, 100_000_000));

        var result = await CopyFileHandler.Handle(
            new CopyFileCommand(
                tenantId,
                Guid.NewGuid(),
                new StorageActorScope(false, null) { IsService = isService },
                original.Id,
                new CopyFileRequest(OwnerType.Customer, customerId, FolderType.Signatures, 2026, Source: source),
                new RequestAuditContext(null, null, "corr-copy")
            ),
            files,
            folders,
            limits,
            new FakeStorageAuditRepository(),
            new DefaultObjectKeyBuilder(),
            new SystemFolderProvisioner(folders),
            new FakeObjectStorage(),
            Options.Create(new CloudStorageOptions()),
            new FakeStoragePlatformSettingsProvider(),
            new FakeUploadPolicyResolver(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        return (await files.GetAsync(tenantId, result.Value.FileId, CancellationToken.None))!;
    }

    private static FileObject SignedOriginal(Guid tenantId)
    {
        var now = DateTime.UtcNow;
        var file = FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Signature,
                Guid.NewGuid(),
                FolderType.Signatures,
                2026,
                ObjectKey.Create($"tenants/{tenantId:N}/signature/{Guid.NewGuid():N}/signatures/2026/s.pdf").Value,
                "signed.pdf",
                "application/pdf",
                1024,
                Guid.NewGuid(),
                now,
                now.AddHours(1)
            )
            .Value;
        file.MarkPendingScan(now);
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", now);
        return file;
    }
}
