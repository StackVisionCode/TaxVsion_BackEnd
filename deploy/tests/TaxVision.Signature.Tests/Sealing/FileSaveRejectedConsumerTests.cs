using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Sealing;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Sealing;

/// <summary>
/// CloudStorage rechazó un archivo que pidió Signature: la solicitud suelta la referencia solo si sigue
/// apuntando a ese FileId, avisa con SealingFailed y el pipeline puede volver a sellar.
/// </summary>
public sealed class FileSaveRejectedConsumerTests
{
    [Fact]
    public async Task A_rejected_sealed_document_returns_to_pending_sealing_and_drops_the_request_certificate()
    {
        var kit = new DeliveryTestKit(
            sendCertificate: true,
            certificateMode: CertificateGenerationMode.SingleForRequest
        ).Seal();
        var federal = kit.Request.Documents[0];
        var state = kit.Request.Documents[1];
        var rejected = federal.SealedFileId!.Value;
        var stateSealed = state.SealedFileId;
        var run = new Run(kit);

        await run.HandleAsync(Rejection(kit, rejected));

        Assert.Null(federal.SealedFileId);
        Assert.Null(federal.DocumentHashPost);
        Assert.Null(federal.SealedAtUtc);
        Assert.Null(kit.Request.CertificateFileId);
        Assert.Equal(stateSealed, state.SealedFileId);
        Assert.False(kit.Request.AllDocumentsSealed());
        Assert.True(kit.Request.IsDocumentReadyForSealing(federal.Id));
        Assert.Equal(1, run.UnitOfWork.SaveCount);
        var failed = Assert.Single(kit.Bus.Messages.OfType<SignatureRequestSealingFailedIntegrationEvent>());
        Assert.Equal(kit.Request.Id, failed.SignatureRequestId);
        Assert.Equal(kit.Request.TenantId, failed.TenantId);
        Assert.Equal("Signature.Storage.SaveRejected", failed.ErrorCode);
        Assert.Contains("signed document", failed.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rejected_sealed_document_drops_only_its_own_document_certificate()
    {
        var kit = new DeliveryTestKit(sendCertificate: true).Seal();
        var federal = kit.Request.Documents[0];
        var state = kit.Request.Documents[1];
        var stateCertificate = state.CertificateFileId;

        await new Run(kit).HandleAsync(Rejection(kit, federal.SealedFileId!.Value));

        Assert.Null(federal.SealedFileId);
        Assert.Null(federal.CertificateFileId);
        Assert.NotNull(state.SealedFileId);
        Assert.Equal(stateCertificate, state.CertificateFileId);
    }

    [Fact]
    public async Task A_rejected_request_certificate_is_released_and_the_documents_stay_sealed()
    {
        var kit = new DeliveryTestKit(
            sendCertificate: true,
            certificateMode: CertificateGenerationMode.SingleForRequest
        ).Seal();

        await new Run(kit).HandleAsync(Rejection(kit, kit.Request.CertificateFileId!.Value));

        Assert.Null(kit.Request.CertificateFileId);
        Assert.True(kit.Request.AllDocumentsSealed());
        Assert.False(kit.Request.AllCertificateArtifactsGenerated());
        var failed = Assert.Single(kit.Bus.Messages.OfType<SignatureRequestSealingFailedIntegrationEvent>());
        Assert.Contains("certificate of completion", failed.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rejected_document_certificate_is_released_alone()
    {
        var kit = new DeliveryTestKit(sendCertificate: true).Seal();
        var federal = kit.Request.Documents[0];
        var state = kit.Request.Documents[1];
        var stateCertificate = state.CertificateFileId;

        await new Run(kit).HandleAsync(Rejection(kit, federal.CertificateFileId!.Value));

        Assert.Null(federal.CertificateFileId);
        Assert.NotNull(federal.SealedFileId);
        Assert.Equal(stateCertificate, state.CertificateFileId);
        Assert.True(kit.Request.AllDocumentsSealed());
        Assert.Single(kit.Bus.Messages.OfType<SignatureRequestSealingFailedIntegrationEvent>());
    }

    [Fact]
    public async Task A_late_rejection_of_a_replaced_file_leaves_the_newer_one_untouched()
    {
        var kit = new DeliveryTestKit().Seal();
        var federal = kit.Request.Documents[0];
        var rejected = federal.SealedFileId!.Value;
        var run = new Run(kit);
        await run.HandleAsync(Rejection(kit, rejected));
        var resealed = Guid.NewGuid();
        Assert.True(
            kit.Request.MarkDocumentSealed(
                federal.Id,
                resealed,
                DocumentHash.Create(new string('d', 64)).Value,
                DateTime.UtcNow
            ).IsSuccess
        );

        await run.HandleAsync(Rejection(kit, rejected));

        Assert.Equal(resealed, federal.SealedFileId);
        Assert.Equal(1, run.UnitOfWork.SaveCount);
        Assert.Single(kit.Bus.Messages.OfType<SignatureRequestSealingFailedIntegrationEvent>());
    }

    [Theory]
    [InlineData("documents")]
    [InlineData("Signature")]
    public async Task A_rejection_requested_by_another_service_is_ignored(string requestingService)
    {
        var kit = new DeliveryTestKit().Seal();
        var sealedFileId = kit.Request.Documents[0].SealedFileId!.Value;
        var run = new Run(kit);

        await run.HandleAsync(Rejection(kit, sealedFileId) with { RequestingService = requestingService });

        Assert.Equal(sealedFileId, kit.Request.Documents[0].SealedFileId);
        Assert.Equal(0, run.UnitOfWork.SaveCount);
        Assert.Empty(kit.Bus.Messages);
    }

    [Fact]
    public async Task An_unknown_file_is_a_no_op()
    {
        var kit = new DeliveryTestKit().Seal();
        var run = new Run(kit);

        await run.HandleAsync(Rejection(kit, Guid.NewGuid()));

        Assert.True(kit.Request.AllDocumentsSealed());
        Assert.Equal(0, run.UnitOfWork.SaveCount);
        Assert.Empty(kit.Bus.Messages);
    }

    [Fact]
    public async Task A_rejection_from_another_tenant_does_not_touch_the_request()
    {
        var kit = new DeliveryTestKit().Seal();
        var sealedFileId = kit.Request.Documents[0].SealedFileId!.Value;
        var run = new Run(kit);

        await run.HandleAsync(Rejection(kit, sealedFileId) with { TenantId = Guid.NewGuid() });

        Assert.Equal(sealedFileId, kit.Request.Documents[0].SealedFileId);
        Assert.Equal(0, run.UnitOfWork.SaveCount);
        Assert.Empty(kit.Bus.Messages);
    }

    [Fact]
    public async Task While_the_request_is_being_sealed_the_rejection_is_retried_without_changes()
    {
        var kit = new DeliveryTestKit().Seal();
        var sealedFileId = kit.Request.Documents[0].SealedFileId!.Value;
        var run = new Run(kit, lockAvailable: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => run.HandleAsync(Rejection(kit, sealedFileId)));

        Assert.Equal(sealedFileId, kit.Request.Documents[0].SealedFileId);
        Assert.Equal(0, run.UnitOfWork.SaveCount);
        Assert.Empty(kit.Bus.Messages);
    }

    [Fact]
    public async Task A_released_sealed_document_requests_deletion_of_the_request_certificate_it_detached()
    {
        var kit = new DeliveryTestKit(
            sendCertificate: true,
            certificateMode: CertificateGenerationMode.SingleForRequest
        ).Seal();
        var certificate = kit.Request.CertificateFileId!.Value;

        await new Run(kit).HandleAsync(Rejection(kit, kit.Request.Documents[0].SealedFileId!.Value));

        var deletion = Assert.Single(kit.Bus.Messages.OfType<FilesDeletionRequestedIntegrationEvent>());
        Assert.Equal(kit.Request.TenantId, deletion.TenantId);
        Assert.Equal("signature", deletion.SourceService);
        Assert.Equal(kit.Request.Id, deletion.SourceReferenceId);
        Assert.Equal("sealed_document_rejected", deletion.Reason);
        var target = Assert.Single(deletion.Files);
        Assert.Equal(certificate, target.FileId);
        Assert.Equal("Signature", target.ExpectedOwnerType);
        Assert.Equal(kit.Request.Id, target.ExpectedOwnerId);
        Assert.Equal("Signatures", target.ExpectedFolderType);
    }

    [Fact]
    public async Task A_released_sealed_document_requests_deletion_of_its_document_certificate_owned_by_the_customer()
    {
        var customerId = Guid.NewGuid();
        var kit = new DeliveryTestKit(sendCertificate: true, firstSignerCustomerId: customerId).Seal();
        var federal = kit.Request.Documents[0];
        var federalCertificate = federal.CertificateFileId!.Value;

        await new Run(kit).HandleAsync(Rejection(kit, federal.SealedFileId!.Value));

        var target = Assert.Single(
            Assert.Single(kit.Bus.Messages.OfType<FilesDeletionRequestedIntegrationEvent>()).Files
        );
        Assert.Equal(federalCertificate, target.FileId);
        Assert.Equal("Customer", target.ExpectedOwnerType);
        Assert.Equal(customerId, target.ExpectedOwnerId);
    }

    [Fact]
    public async Task Portal_copies_of_a_detached_certificate_are_deleted_with_it()
    {
        var ana = Guid.NewGuid();
        var luis = Guid.NewGuid();
        var kit = new DeliveryTestKit(
            sendCertificate: true,
            certificateMode: CertificateGenerationMode.SingleForRequest,
            firstSignerCustomerId: ana,
            secondSignerCustomerId: luis
        ).Seal();
        var certificate = kit.Request.CertificateFileId!.Value;
        var anaCopy = Guid.NewGuid();
        Assert.True(kit.Request.RecordCustomerCopy(certificate, ana, anaCopy, DateTime.UtcNow).IsSuccess);
        Assert.True(
            kit.Request.RecordCustomerCopy(
                kit.Request.Documents[1].SealedFileId!.Value,
                luis,
                Guid.NewGuid(),
                DateTime.UtcNow
            ).IsSuccess
        );

        await new Run(kit).HandleAsync(Rejection(kit, kit.Request.Documents[0].SealedFileId!.Value));

        var files = Assert.Single(kit.Bus.Messages.OfType<FilesDeletionRequestedIntegrationEvent>()).Files;
        Assert.Equal(2, files.Count);
        Assert.Contains(files, file => file.FileId == certificate && file.ExpectedOwnerType == "Signature");
        Assert.Contains(
            files,
            file =>
                file.FileId == anaCopy
                && file.ExpectedOwnerType == "Customer"
                && file.ExpectedOwnerId == ana
                && file.ExpectedFolderType == "Signatures"
        );
    }

    [Fact]
    public async Task A_rejected_certificate_alone_orphans_nothing_and_requests_no_deletion()
    {
        var kit = new DeliveryTestKit(
            sendCertificate: true,
            certificateMode: CertificateGenerationMode.SingleForRequest
        ).Seal();

        await new Run(kit).HandleAsync(Rejection(kit, kit.Request.CertificateFileId!.Value));

        Assert.Empty(kit.Bus.Messages.OfType<FilesDeletionRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task A_replayed_rejection_does_not_request_the_deletion_twice()
    {
        var kit = new DeliveryTestKit(sendCertificate: true).Seal();
        var rejection = Rejection(kit, kit.Request.Documents[0].SealedFileId!.Value);
        var run = new Run(kit);

        await run.HandleAsync(rejection);
        await run.HandleAsync(rejection);

        Assert.Single(kit.Bus.Messages.OfType<FilesDeletionRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task A_request_under_legal_hold_keeps_its_detached_certificates()
    {
        var kit = new DeliveryTestKit(sendCertificate: true).Seal();
        Assert.True(kit.Request.PlaceLegalHold(Guid.NewGuid(), "Audit in progress").IsSuccess);

        await new Run(kit).HandleAsync(Rejection(kit, kit.Request.Documents[0].SealedFileId!.Value));

        Assert.Null(kit.Request.Documents[0].SealedFileId);
        Assert.Empty(kit.Bus.Messages.OfType<FilesDeletionRequestedIntegrationEvent>());
    }

    private static FileSaveRejectedIntegrationEvent Rejection(DeliveryTestKit kit, Guid fileId) =>
        new()
        {
            TenantId = kit.Request.TenantId,
            FileId = fileId,
            RequestingService = SignatureStorage.RequestingService,
            OwnerType = "Signature",
            OwnerId = kit.Request.Id,
            FolderType = "Signatures",
            Reason = "StorageQuota.Exceeded",
        };

    private sealed class Run(DeliveryTestKit kit, bool lockAvailable = true)
    {
        public CountingUnitOfWork UnitOfWork { get; } = new();

        public Task HandleAsync(FileSaveRejectedIntegrationEvent evt) =>
            FileSaveRejectedConsumer.Handle(
                evt,
                new SingleRequestRepository(kit.Request),
                new FixedLock(lockAvailable),
                UnitOfWork,
                kit.Bus,
                new FixedCorrelation(),
                NullLogger<SignatureRequest>.Instance,
                CancellationToken.None
            );
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FixedLock(bool acquired) : IDistributedLock
    {
        public Task<ILockHandle> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
            Task.FromResult<ILockHandle>(new Handle(key, acquired));

        private sealed class Handle(string key, bool acquired) : ILockHandle
        {
            public bool IsAcquired => acquired;
            public string Key => key;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
