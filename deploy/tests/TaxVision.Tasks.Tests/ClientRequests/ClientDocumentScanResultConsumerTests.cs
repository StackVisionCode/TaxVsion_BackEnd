using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.Application.ClientRequests.Consumers;
using TaxVision.Tasks.Domain.ClientRequests;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.ClientRequests;

/// <summary>BB-16: el aviso de un documento rechazado sale después de guardar, no antes.</summary>
public sealed class ClientDocumentScanResultConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task An_infected_document_is_rejected_and_announced_after_saving()
    {
        var (request, fileId) = RequestWithDocument();
        var bus = new FakeMessageBus();
        var unitOfWork = new ProbeUnitOfWork(bus);

        await ClientDocumentScanResultConsumer.Handle(
            new FileInfectedDetectedIntegrationEvent
            {
                ObjectKey = "k",
                ScanReport = "eicar",
                TenantId = TenantId,
                FileId = fileId,
                CorrelationId = "corr",
            },
            new InMemoryClientRequestRepository(request),
            unitOfWork,
            bus,
            new NoOpCorrelationContext(),
            NullLogger<ClientRequest>.Instance,
            CancellationToken.None
        );

        Assert.Equal(AttachmentStatus.Rejected, request.Documents.Single().Status);
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Equal(0, unitOfWork.PublishedAtSave);
        var rejected = Assert.IsType<ClientRequestDocumentRejectedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(request.Id, rejected.ClientRequestId);
        Assert.Equal(fileId, rejected.FileId);
        Assert.Equal("W-2.pdf", rejected.DisplayName);
        Assert.Equal("infected", rejected.Reason);
        Assert.Equal("No pudimos procesar este archivo. Volvé a subirlo, por favor.", rejected.ClientMessage);
    }

    [Fact]
    public async Task A_document_of_another_tenant_changes_nothing()
    {
        var (request, fileId) = RequestWithDocument();
        var bus = new FakeMessageBus();
        var unitOfWork = new ProbeUnitOfWork(bus);

        await ClientDocumentScanResultConsumer.Handle(
            new FileBlockedByPolicyIntegrationEvent
            {
                ObjectKey = "k",
                PolicyReason = "policy",
                CreatedBy = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                FileId = fileId,
                CorrelationId = "corr",
            },
            new InMemoryClientRequestRepository(request),
            unitOfWork,
            bus,
            new NoOpCorrelationContext(),
            NullLogger<ClientRequest>.Instance,
            CancellationToken.None
        );

        Assert.Equal(AttachmentStatus.Pending, request.Documents.Single().Status);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.Saves);
    }

    private static (ClientRequest Request, Guid FileId) RequestWithDocument()
    {
        var request = ClientRequest
            .Create(TenantId, Guid.NewGuid(), Guid.NewGuid(), null, "W-2 2025", null, null, Now)
            .Value;
        var fileId = Guid.NewGuid();
        Assert.True(request.SubmitDocument(fileId, "W-2.pdf", "application/pdf", 512, Now).IsSuccess);
        return (request, fileId);
    }

    private sealed class ProbeUnitOfWork(FakeMessageBus bus) : IUnitOfWork
    {
        public int Saves { get; private set; }
        public int PublishedAtSave { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            PublishedAtSave = bus.Published.Count;
            return Task.FromResult(1);
        }
    }
}
