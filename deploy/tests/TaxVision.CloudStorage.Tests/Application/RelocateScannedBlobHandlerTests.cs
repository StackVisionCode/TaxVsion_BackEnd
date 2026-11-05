using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.Commands;

namespace TaxVision.CloudStorage.Tests.Application;

public sealed class RelocateScannedBlobHandlerTests
{
    private const string ObjectKey = "tenants/abc/tenant/documents/2026/file.pdf";
    private readonly CloudStorageOptions _options = new();
    private readonly FakeObjectStorage _storage = new();
    private readonly FakeStorageAuditRepository _audit = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Handle_SourceMissingTargetPresent_IsNoOp()
    {
        _storage.Existing.Add((_options.QuarantineBucket, ObjectKey));

        await HandleAsync(_options.TempBucket, _options.QuarantineBucket);

        Assert.Empty(_storage.Operations);
        Assert.Empty(_audit.Logs);
    }

    [Fact]
    public async Task Handle_BothMissing_LogsMissingWithoutThrowing()
    {
        await HandleAsync(_options.TempBucket, _options.QuarantineBucket);

        Assert.Empty(_storage.Operations);
        var log = Assert.Single(_audit.Logs);
        Assert.Equal("blob.relocate", log.Action);
        Assert.Equal("missing", log.Outcome);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_SourcePresent_CopiesThenDeletes()
    {
        _storage.Existing.Add((_options.TempBucket, ObjectKey));

        await HandleAsync(_options.TempBucket, _options.QuarantineBucket);

        Assert.Equal(
            [$"copy:{_options.TempBucket}->{_options.QuarantineBucket}", $"delete:{_options.TempBucket}"],
            _storage.Operations
        );
    }

    [Fact]
    public async Task Handle_DeleteOnlyWithSourceAlreadyGone_IsNoOp()
    {
        await HandleAsync(_options.TempBucket, null);

        Assert.Empty(_storage.Operations);
        Assert.Empty(_audit.Logs);
    }

    private Task HandleAsync(string source, string? target) =>
        RelocateScannedBlobHandler.Handle(
            new RelocateScannedBlobCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                ObjectKey,
                source,
                target,
                Guid.NewGuid(),
                "corr-relocate"
            ),
            _storage,
            _audit,
            new FakeSystemClock(DateTime.UtcNow),
            _unitOfWork,
            NullLogger<RelocateScannedBlobCommand>.Instance,
            CancellationToken.None
        );
}
