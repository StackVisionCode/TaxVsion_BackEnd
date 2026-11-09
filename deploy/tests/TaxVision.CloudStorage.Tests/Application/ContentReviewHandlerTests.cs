using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Application.Moderation;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Moderation;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Tests.Domain;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>Cola de revisión de PlatformAdmin: aprobar, rechazar, reportar y la vista previa auditada.</summary>
public sealed class ContentReviewHandlerTests
{
    private static readonly RequestAuditContext Audit = new("127.0.0.1", "tests", "corr-review");
    private readonly CloudStorageOptions _options = new();
    private readonly FakeFileObjectRepository _files = new();
    private readonly FakeStorageLimitRepository _limits = new();
    private readonly FakeObjectStorage _storage = new();
    private readonly FakeStorageAuditRepository _audit = new();
    private readonly FakeMessageBus _bus = new();
    private readonly FakeSystemClock _clock = new(DateTime.UtcNow);

    private FileObject Seed(FolderType folder = FolderType.Other, long quota = 1_000)
    {
        var file = FileObjectReviewTransitionTests.PendingReview(folder: folder);
        _files.Seed(file);
        _limits.Seed(TenantStorageLimit.Create(file.TenantId, "starter", quota, quota));
        _storage.Existing.Add((_options.QuarantineBucket, file.ObjectKey));
        return file;
    }

    private async Task<BuildingBlocks.Results.Result> ApproveAsync(FileObject file)
    {
        file.ClearDomainEvents();
        var result = await ApproveContentReviewHandler.Handle(
            new ApproveContentReviewCommand(file.TenantId, file.Id, Guid.NewGuid(), Audit),
            _files,
            _limits,
            _storage,
            _audit,
            Options.Create(_options),
            _clock,
            new FakeUnitOfWork(),
            CancellationToken.None
        );
        await DomainEventReplay.DispatchIntoAsync(_bus, file);
        return result;
    }

    // CST-9: la red contra el aviso doble. Si vuelve la publicación a mano, aquí salen dos.
    [Fact]
    public async Task ApproveReview_EmitsExactlyOneFileAvailable()
    {
        var file = Seed();

        await ApproveAsync(file);

        var available = Assert.Single(_bus.Published.OfType<FileAvailableIntegrationEvent>());
        Assert.Equal(file.Id, available.FileId);
        Assert.Equal(file.TenantId, available.TenantId);
        Assert.Equal("corr-review", available.CorrelationId);
        Assert.Empty(_bus.Published.OfType<CustomerDocumentUploadedIntegrationEvent>());
    }

    [Fact]
    public async Task Approving_moves_the_file_out_of_quarantine_counts_it_and_publishes_it()
    {
        var file = Seed(FolderType.Avatars);

        var result = await ApproveAsync(file);

        Assert.True(result.IsSuccess);
        Assert.Equal(FileStatus.Available, file.Status);
        Assert.Contains(
            (_options.QuarantineBucket, file.ObjectKey, _options.MainBucket, file.ObjectKey),
            _storage.Copied
        );
        Assert.Contains((_options.QuarantineBucket, file.ObjectKey), _storage.Deleted);
        Assert.Single(_storage.Tagged); // avatar: regla de lifecycle de versiones
        Assert.Equal(file.SizeBytes, (await _limits.GetAsync(file.TenantId, default))!.UsedBytes);
        var available = Assert.Single(_bus.Published.OfType<FileAvailableIntegrationEvent>());
        Assert.Equal("image/jpeg", available.ContentType);
        Assert.Equal(file.ChecksumSha256, available.ChecksumSha256);
        Assert.Contains(_audit.Logs, l => l.Action == "moderation.review" && l.Outcome == "approved");
    }

    [Fact]
    public async Task Approval_fails_without_quota_and_leaves_the_file_in_review()
    {
        var file = Seed(quota: 5);

        var result = await ApproveAsync(file);

        Assert.Equal(QuotaErrors.Exceeded, result.Error);
        Assert.Equal(FileStatus.PendingReview, file.Status);
        Assert.Empty(_storage.Copied);
    }

    [Fact]
    public async Task A_retry_after_the_object_moved_does_not_copy_again()
    {
        var file = Seed();
        _storage.Existing.Remove((_options.QuarantineBucket, file.ObjectKey));

        Assert.True((await ApproveAsync(file)).IsSuccess);
        Assert.Empty(_storage.Copied);
    }

    // Una aprobación que llega antes de que RelocateScannedBlob mueva el objeto no termina con el archivo en cuarentena.
    [Fact]
    public async Task Approval_before_the_relocation_leaves_the_object_in_main()
    {
        var file = Seed();
        _storage.Existing.Remove((_options.QuarantineBucket, file.ObjectKey));
        _storage.Existing.Add((_options.TempBucket, file.ObjectKey));

        Assert.True((await ApproveAsync(file)).IsSuccess);
        await RelocateScannedBlobHandler.Handle(
            new RelocateScannedBlobCommand(
                file.TenantId,
                file.Id,
                file.ObjectKey,
                _options.TempBucket,
                _options.QuarantineBucket,
                Guid.NewGuid(),
                "corr-scan"
            ),
            _files,
            Options.Create(_options),
            _storage,
            _audit,
            _clock,
            new FakeUnitOfWork(),
            NullLogger<RelocateScannedBlobCommand>.Instance,
            CancellationToken.None
        );

        Assert.Equal(FileStatus.Available, file.Status);
        Assert.Contains(_storage.Copied, copy => copy.DestinationBucket == _options.MainBucket);
        Assert.DoesNotContain(_storage.Copied, copy => copy.DestinationBucket == _options.QuarantineBucket);
    }

    [Fact]
    public async Task Rejecting_blocks_holds_and_notifies()
    {
        var file = Seed();

        file.ClearDomainEvents();
        var result = await RejectContentReviewHandler.Handle(
            new RejectContentReviewCommand(file.TenantId, file.Id, Guid.NewGuid(), new string('n', 3000), Audit),
            _files,
            _audit,
            _clock,
            new FakeUnitOfWork(),
            CancellationToken.None
        );
        await DomainEventReplay.DispatchIntoAsync(_bus, file);

        Assert.True(result.IsSuccess);
        Assert.Equal(FileStatus.BlockedByPolicy, file.Status);
        Assert.True(file.IsLegalHeld);
        Assert.Single(_bus.Published.OfType<FileBlockedByPolicyIntegrationEvent>());
        Assert.Equal(RejectContentReviewHandler.NotesMaxLength, _audit.Logs.Single().Details!.Length);
    }

    [Fact]
    public async Task Reporting_holds_once_and_audits_every_time()
    {
        var file = Seed();
        var command = new ReportContentReviewFileCommand(
            file.TenantId,
            file.Id,
            Guid.NewGuid(),
            "CyberTipline #1",
            Audit
        );

        var first = await ReportContentReviewFileHandler.Handle(
            command,
            _files,
            _audit,
            _clock,
            new FakeUnitOfWork(),
            CancellationToken.None
        );
        var second = await ReportContentReviewFileHandler.Handle(
            command,
            _files,
            _audit,
            _clock,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(first.Value.LegalHoldPlaced);
        Assert.False(second.Value.LegalHoldPlaced);
        Assert.True(file.IsLegalHeld);
        Assert.Equal(2, _audit.Logs.Count(l => l.Action == "moderation.reported"));
    }

    [Fact]
    public async Task Preview_signs_the_quarantine_copy_inline_and_is_audited()
    {
        var file = Seed();

        var result = await PreviewContentReviewFileHandler.Handle(
            new PreviewContentReviewFileCommand(file.TenantId, file.Id, Guid.NewGuid(), Audit),
            _files,
            _storage,
            _audit,
            Options.Create(_options),
            new FakeStoragePlatformSettingsProvider(),
            _clock,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal((_options.QuarantineBucket, file.ObjectKey, "inline"), _storage.PresignedWithDisposition.Single());
        Assert.Contains(_audit.Logs, l => l.Action == "moderation.preview");
    }

    [Fact]
    public async Task Files_outside_moderation_are_refused()
    {
        var file = Seed();
        Assert.True((await ApproveAsync(file)).IsSuccess);

        Assert.Equal(ContentModerationErrors.NotUnderModeration, (await ApproveAsync(file)).Error);
        var queue = await ListContentReviewQueueHandler.Handle(
            new ListContentReviewQueueQuery(FileStatus.Available, 0, 10),
            new EmptyQueue(),
            CancellationToken.None
        );
        Assert.Equal(ContentModerationErrors.NotUnderModeration, queue.Error);
    }

    private sealed class EmptyQueue : IContentReviewQueue
    {
        public Task<IReadOnlyList<FileObject>> ListAsync(FileStatus status, int skip, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FileObject>>([]);
    }
}
