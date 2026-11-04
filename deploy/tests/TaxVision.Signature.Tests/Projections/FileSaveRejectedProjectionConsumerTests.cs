using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Signature.Application.Projections.CloudStorageEvents;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Projections;

/// <summary>Un guardado rechazado deja la proyección en Rejected, sin degradar un archivo disponible.</summary>
public sealed class FileSaveRejectedProjectionConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Theory]
    [InlineData("signature")]
    [InlineData("documents")]
    public async Task A_rejection_without_a_projection_creates_a_rejected_row(string requestingService)
    {
        var files = new InMemoryFiles();
        var fileId = Guid.NewGuid();
        var unitOfWork = new SavingUnitOfWork();

        await Handle(files, unitOfWork, fileId, requestingService);

        var projection = await files.GetByFileIdAsync(TenantId, fileId);
        Assert.Equal(FileScanStatus.Rejected, projection!.Status);
        Assert.Equal("EmptyFile", projection.RejectionReason);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_late_rejection_leaves_an_available_file_available()
    {
        var files = new InMemoryFiles();
        var fileId = Guid.NewGuid();
        files.Available(TenantId, fileId, 10);

        await Handle(files, new SavingUnitOfWork(), fileId, "signature");

        Assert.Equal(FileScanStatus.Available, (await files.GetByFileIdAsync(TenantId, fileId))!.Status);
    }

    [Fact]
    public async Task A_redelivered_rejection_keeps_a_single_row()
    {
        var files = new InMemoryFiles();
        var fileId = Guid.NewGuid();

        await Handle(files, new SavingUnitOfWork(), fileId, "signature");
        await Handle(files, new SavingUnitOfWork(), fileId, "signature");

        Assert.Equal(FileScanStatus.Rejected, (await files.GetByFileIdAsync(TenantId, fileId))!.Status);
    }

    private static Task Handle(
        InMemoryFiles files,
        SavingUnitOfWork unitOfWork,
        Guid fileId,
        string requestingService
    ) =>
        FileSaveRejectedProjectionConsumer.Handle(
            new FileSaveRejectedIntegrationEvent
            {
                TenantId = TenantId,
                FileId = fileId,
                RequestingService = requestingService,
                Reason = "EmptyFile",
            },
            files,
            unitOfWork,
            new FixedCorrelation(),
            CancellationToken.None
        );
}
