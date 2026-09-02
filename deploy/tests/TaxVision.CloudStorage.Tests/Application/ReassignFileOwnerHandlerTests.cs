using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// Fase 3b — ReassignFileOwnerHandler: re-asigna el dueno logico de un archivo (Signature → Customer)
/// y lo re-archiva en la carpeta de sistema del nuevo dueno. Idempotente.
/// </summary>
public sealed class ReassignFileOwnerHandlerTests
{
    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "corr-1";

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new Noop();
        }

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }

    private static FileObject SignatureOwned(Guid tenantId, Guid signatureId)
    {
        var key = ObjectKey
            .Create($"tenants/{tenantId:N}/signature/{signatureId:N}/signatures/2026/{Guid.NewGuid():N}.pdf")
            .Value;
        return FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Signature,
                signatureId,
                FolderType.Signatures,
                2026,
                key,
                "signed.pdf",
                "application/pdf",
                1024,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow
            )
            .Value;
    }

    private static ReassignFileOwnerRequestedIntegrationEvent Evt(Guid tenantId, Guid fileId, Guid customerId) =>
        new()
        {
            TenantId = tenantId,
            FileId = fileId,
            NewOwnerType = "Customer",
            NewOwnerId = customerId,
            ActorId = Guid.NewGuid(),
            CorrelationId = "corr-1",
        };

    [Fact]
    public async Task Reassigns_owner_to_customer_and_files_into_signed_documents()
    {
        var tenantId = Guid.NewGuid();
        var signatureId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var file = SignatureOwned(tenantId, signatureId);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();

        await ReassignFileOwnerHandler.Handle(
            Evt(tenantId, file.Id, customerId),
            files,
            new SystemFolderProvisioner(folders),
            Options.Create(new CloudStorageOptions()),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<ReassignFileOwnerRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        var updated = await files.GetAsync(tenantId, file.Id, CancellationToken.None);
        Assert.Equal(OwnerType.Customer, updated!.OwnerType);
        Assert.Equal(customerId, updated.OwnerId);
        var folder = await folders.GetByOwnerAndCategoryAsync(
            tenantId,
            OwnerType.Customer,
            customerId,
            "sys.signatures",
            CancellationToken.None
        );
        Assert.NotNull(folder);
        Assert.Equal(folder!.Id, updated.FolderId);
    }

    [Fact]
    public async Task Unknown_file_is_a_no_op()
    {
        var tenantId = Guid.NewGuid();
        var files = new FakeFileObjectRepository();

        // No debe lanzar.
        await ReassignFileOwnerHandler.Handle(
            Evt(tenantId, Guid.NewGuid(), Guid.NewGuid()),
            files,
            new SystemFolderProvisioner(new FakeFolderRepository()),
            Options.Create(new CloudStorageOptions()),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<ReassignFileOwnerRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );
    }
}
