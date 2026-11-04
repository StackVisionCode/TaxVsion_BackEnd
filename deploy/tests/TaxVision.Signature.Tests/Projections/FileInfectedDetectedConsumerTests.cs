using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Projections.CloudStorageEvents;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Projections;

public sealed class FileInfectedDetectedConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task An_infected_file_without_a_projection_is_recorded_as_infected()
    {
        var fileId = Guid.NewGuid();
        var files = new InMemoryFiles();
        var unitOfWork = new SavingUnitOfWork();

        await Handle(files, unitOfWork, fileId);

        var projection = await files.GetByFileIdAsync(TenantId, fileId);
        Assert.Equal(FileScanStatus.Infected, projection!.Status);
        Assert.Equal("Eicar", projection.RejectionReason);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task An_existing_projection_is_marked_infected()
    {
        var fileId = Guid.NewGuid();
        var files = new InMemoryFiles();
        files.Available(TenantId, fileId, 10);

        await Handle(files, new SavingUnitOfWork(), fileId);

        Assert.Equal(FileScanStatus.Infected, (await files.GetByFileIdAsync(TenantId, fileId))!.Status);
    }

    private static Task Handle(InMemoryFiles files, SavingUnitOfWork unitOfWork, Guid fileId) =>
        FileInfectedDetectedConsumer.Handle(
            new FileInfectedDetectedIntegrationEvent
            {
                TenantId = TenantId,
                FileId = fileId,
                ObjectKey = "t/file.pdf",
                ScanReport = "Eicar",
            },
            files,
            unitOfWork,
            new FixedCorrelation(),
            NullLogger<FileMetadataRef>.Instance,
            CancellationToken.None
        );
}
