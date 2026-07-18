using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Infrastructure.Storage;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>Fase D0 — SaveFileFromSourceHandler: reemplaza el HTTP+M2M initiate/PUT/complete para servicios de negocio.</summary>
public sealed class SaveFileFromSourceHandlerTests
{
    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "corr-1";

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new NoopScope();
        }

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }
    }

    private static SaveFileRequestedIntegrationEvent Evt(Guid tenantId, Guid fileId, Action<Builder>? configure = null)
    {
        var builder = new Builder(tenantId, fileId);
        configure?.Invoke(builder);
        return builder.Build();
    }

    private sealed class Builder(Guid tenantId, Guid fileId)
    {
        public string OwnerType { get; set; } = "Signature";
        public Guid? OwnerId { get; set; } = Guid.NewGuid();
        public string FolderType { get; set; } = "Signatures";
        public int? TaxYear { get; set; } = 2026;
        public string OriginalName { get; set; } = "signed-report.pdf";
        public string ContentType { get; set; } = "application/pdf";
        public long SizeBytes { get; set; } = 1024;

        public SaveFileRequestedIntegrationEvent Build() =>
            new()
            {
                TenantId = tenantId,
                FileId = fileId,
                RequestingService = "signature",
                SourceBucket = "taxvision-temp",
                SourceObjectKey = $"signature/{fileId:N}/signed-report.pdf",
                ActorId = Guid.NewGuid(),
                OwnerType = OwnerType,
                OwnerId = OwnerId,
                FolderType = FolderType,
                TaxYear = TaxYear,
                OriginalName = OriginalName,
                ContentType = ContentType,
                SizeBytes = SizeBytes,
                CorrelationId = "corr-1",
            };
    }

    private static IOptions<CloudStorageOptions> DefaultOptions() =>
        Microsoft.Extensions.Options.Options.Create(new CloudStorageOptions());

    [Fact]
    public async Task Valid_event_registers_the_file_copies_it_and_triggers_the_scan()
    {
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var files = new FakeFileObjectRepository();
        var limits = new FakeStorageLimitRepository();
        limits.Seed(TenantStorageLimit.Create(tenantId, "starter", maxBytes: 1_000_000, maxFileSizeBytes: 1_000_000));
        var audit = new FakeStorageAuditRepository();
        var storage = new FakeObjectStorage();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        var evt = Evt(tenantId, fileId);

        await SaveFileFromSourceHandler.Handle(
            evt,
            files,
            limits,
            audit,
            new DefaultObjectKeyBuilder(),
            storage,
            DefaultOptions(),
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            NullLogger<SaveFileRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        var registered = await files.GetAsync(tenantId, fileId, CancellationToken.None);
        Assert.NotNull(registered);
        Assert.Equal(FileStatus.PendingScan, registered!.Status);
        Assert.Single(
            storage.Copied,
            copy => copy.SourceBucket == evt.SourceBucket && copy.SourceObjectKey == evt.SourceObjectKey
        );
        Assert.Single(
            storage.Deleted,
            deleted => deleted.Bucket == evt.SourceBucket && deleted.ObjectKey == evt.SourceObjectKey
        );
        Assert.Single(audit.Logs, log => log.Action == "upload.from-source" && log.Details == "service=signature");
        Assert.Single(bus.Published.OfType<ScanFileCommand>(), cmd => cmd.FileId == fileId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Redelivery_of_an_already_registered_file_is_a_no_op()
    {
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var files = new FakeFileObjectRepository();
        var key = ObjectKey
            .Create($"tenants/{tenantId:N}/signature/{Guid.NewGuid():N}/signatures/2026/{fileId:N}.pdf")
            .Value;
        var existing = FileObject
            .Register(
                fileId,
                tenantId,
                OwnerType.Signature,
                Guid.NewGuid(),
                FolderType.Signatures,
                2026,
                key,
                "signed-report.pdf",
                "application/pdf",
                1024,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow
            )
            .Value;
        files.Seed(existing);
        var limits = new FakeStorageLimitRepository();
        var storage = new FakeObjectStorage();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        await SaveFileFromSourceHandler.Handle(
            Evt(tenantId, fileId),
            files,
            limits,
            new FakeStorageAuditRepository(),
            new DefaultObjectKeyBuilder(),
            storage,
            DefaultOptions(),
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            NullLogger<SaveFileRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Empty(storage.Copied);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Invalid_OwnerType_is_dropped_without_touching_quota_or_storage()
    {
        var tenantId = Guid.NewGuid();
        var limits = new FakeStorageLimitRepository();
        limits.Seed(TenantStorageLimit.Create(tenantId, "starter", maxBytes: 1_000_000, maxFileSizeBytes: 1_000_000));
        var storage = new FakeObjectStorage();
        var unitOfWork = new FakeUnitOfWork();

        await SaveFileFromSourceHandler.Handle(
            Evt(tenantId, Guid.NewGuid(), b => b.OwnerType = "NotARealOwnerType"),
            new FakeFileObjectRepository(),
            limits,
            new FakeStorageAuditRepository(),
            new DefaultObjectKeyBuilder(),
            storage,
            DefaultOptions(),
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            NullLogger<SaveFileRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Empty(storage.Copied);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Missing_quota_provisioning_is_dropped()
    {
        var tenantId = Guid.NewGuid();
        var storage = new FakeObjectStorage();
        var unitOfWork = new FakeUnitOfWork();

        await SaveFileFromSourceHandler.Handle(
            Evt(tenantId, Guid.NewGuid()),
            new FakeFileObjectRepository(),
            new FakeStorageLimitRepository(), // sin seed => sin cuota provisionada
            new FakeStorageAuditRepository(),
            new DefaultObjectKeyBuilder(),
            storage,
            DefaultOptions(),
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            NullLogger<SaveFileRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Empty(storage.Copied);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Extension_rejected_by_the_upload_policy_is_dropped_and_releases_nothing()
    {
        var tenantId = Guid.NewGuid();
        var limit = TenantStorageLimit.Create(tenantId, "starter", maxBytes: 1_000_000, maxFileSizeBytes: 1_000_000);
        var limits = new FakeStorageLimitRepository();
        limits.Seed(limit);
        var storage = new FakeObjectStorage();
        var unitOfWork = new FakeUnitOfWork();

        await SaveFileFromSourceHandler.Handle(
            Evt(
                tenantId,
                Guid.NewGuid(),
                b =>
                {
                    b.OriginalName = "malware.exe";
                    b.ContentType = "application/octet-stream";
                }
            ),
            new FakeFileObjectRepository(),
            limits,
            new FakeStorageAuditRepository(),
            new DefaultObjectKeyBuilder(),
            storage,
            DefaultOptions(),
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            NullLogger<SaveFileRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Empty(storage.Copied);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Equal(0, limit.ReservedBytes);
    }

    [Fact]
    public async Task Quota_exceeded_is_dropped()
    {
        var tenantId = Guid.NewGuid();
        var limits = new FakeStorageLimitRepository();
        limits.Seed(TenantStorageLimit.Create(tenantId, "starter", maxBytes: 100, maxFileSizeBytes: 1_000_000));
        var storage = new FakeObjectStorage();
        var unitOfWork = new FakeUnitOfWork();

        await SaveFileFromSourceHandler.Handle(
            Evt(tenantId, Guid.NewGuid(), b => b.SizeBytes = 10_000),
            new FakeFileObjectRepository(),
            limits,
            new FakeStorageAuditRepository(),
            new DefaultObjectKeyBuilder(),
            storage,
            DefaultOptions(),
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            NullLogger<SaveFileRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Empty(storage.Copied);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
