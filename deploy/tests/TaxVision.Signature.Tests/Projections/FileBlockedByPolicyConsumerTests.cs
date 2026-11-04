using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Signature.Application.Projections.CloudStorageEvents;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Projections;

public sealed class FileBlockedByPolicyConsumerTests
{
    [Fact]
    public async Task A_blocked_file_is_recorded_as_rejected_with_the_policy_reason()
    {
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var files = new InMemoryFiles();
        var unitOfWork = new SavingUnitOfWork();

        await FileBlockedByPolicyConsumer.Handle(
            new FileBlockedByPolicyIntegrationEvent
            {
                TenantId = tenantId,
                FileId = fileId,
                ObjectKey = "q/file.pdf",
                PolicyReason = "Explicit content",
                CreatedBy = Guid.NewGuid(),
            },
            files,
            unitOfWork,
            new FixedCorrelation(),
            CancellationToken.None
        );

        var projection = await files.GetByFileIdAsync(tenantId, fileId);
        Assert.Equal(FileScanStatus.Rejected, projection!.Status);
        Assert.Equal("Explicit content", projection.RejectionReason);
        Assert.Equal(1, unitOfWork.SaveCount);
    }
}
