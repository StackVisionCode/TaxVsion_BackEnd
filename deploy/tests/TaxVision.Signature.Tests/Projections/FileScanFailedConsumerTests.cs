using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Signature.Application.Projections.CloudStorageEvents;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Projections;

public sealed class FileScanFailedConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task A_failed_scan_is_recorded_as_rejected()
    {
        var fileId = Guid.NewGuid();
        var files = new InMemoryFiles();

        await Handle(files, fileId);

        var projection = await files.GetByFileIdAsync(TenantId, fileId);
        Assert.Equal(FileScanStatus.Rejected, projection!.Status);
        Assert.Equal("content_type_mismatch", projection.RejectionReason);
    }

    [Fact]
    public async Task A_failed_scan_does_not_downgrade_an_available_file()
    {
        var fileId = Guid.NewGuid();
        var files = new InMemoryFiles();
        files.Available(TenantId, fileId, 10);

        await Handle(files, fileId);

        Assert.Equal(FileScanStatus.Available, (await files.GetByFileIdAsync(TenantId, fileId))!.Status);
    }

    private static Task Handle(InMemoryFiles files, Guid fileId) =>
        FileScanFailedConsumer.Handle(
            new FileScanFailedIntegrationEvent
            {
                TenantId = TenantId,
                FileId = fileId,
                ObjectKey = "t/file.pdf",
                Reason = "content_type_mismatch",
            },
            files,
            new SavingUnitOfWork(),
            new FixedCorrelation(),
            CancellationToken.None
        );
}
