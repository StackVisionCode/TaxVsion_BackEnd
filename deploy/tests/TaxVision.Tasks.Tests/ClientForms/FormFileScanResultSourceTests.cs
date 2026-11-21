using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Consumers;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Tests.Dependencies;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.ClientForms;

/// <summary>Solo el veredicto que deja el adjunto disponible lo declara a CloudStorage, y una sola vez.</summary>
public sealed class FormFileScanResultSourceTests
{
    [Fact]
    public async Task A_verdict_that_makes_the_attachment_available_declares_it()
    {
        var fileId = Guid.NewGuid();
        var request = CustomerRequest();
        FormFileSourcesTests.Register(request, fileId);
        var bus = new FakeMessageBus();

        await AvailableAsync(request, fileId, bus, new MemoryFormFileScanVerdictRepository());

        var declared = Assert.Single(bus.Published.OfType<FileSourceDeclaredIntegrationEvent>());
        Assert.Equal(
            (fileId, "form_request", request.Id, "attachment", (Guid?)CustomerId),
            (
                declared.FileId,
                declared.SourceResourceType,
                declared.SourceResourceId,
                declared.SourceRole,
                declared.RelatedCustomerId
            )
        );
    }

    [Fact]
    public async Task An_attachment_that_was_already_available_is_not_declared_again()
    {
        var fileId = Guid.NewGuid();
        var request = CustomerRequest();
        FormFileSourcesTests.Register(request, fileId);
        var verdicts = new MemoryFormFileScanVerdictRepository();
        await AvailableAsync(request, fileId, new FakeMessageBus(), verdicts);
        var bus = new FakeMessageBus();

        await AvailableAsync(request, fileId, bus, verdicts);

        Assert.Empty(bus.Published.OfType<FileSourceDeclaredIntegrationEvent>());
    }

    [Fact]
    public async Task Rejected_or_held_for_review_is_not_declared()
    {
        var infectedFile = Guid.NewGuid();
        var reviewFile = Guid.NewGuid();
        var request = CustomerRequest();
        FormFileSourcesTests.Register(request, infectedFile);
        FormFileSourcesTests.Register(request, reviewFile);
        var bus = new FakeMessageBus();
        var repository = new SingleFormRequestRepository(request);
        var verdicts = new MemoryFormFileScanVerdictRepository();

        await FormFileScanResultConsumer.Handle(
            new FileInfectedDetectedIntegrationEvent
            {
                TenantId = TenantId,
                FileId = infectedFile,
                ObjectKey = "objects/return.pdf",
                ScanReport = "EICAR",
                OwnerType = "Customer",
                OwnerId = CustomerId,
                FolderType = "ClientForms",
                OccurredOn = Now,
            },
            repository,
            verdicts,
            new FixedCustomerReader(),
            new RecordingUnitOfWork(),
            bus,
            new TestCorrelationContext(),
            NullLogger<FormRequest>.Instance,
            new RecordingClientFormsMetrics(),
            default
        );
        await FormFileScanResultConsumer.Handle(
            new FilePendingReviewIntegrationEvent
            {
                TenantId = TenantId,
                FileId = reviewFile,
                ObjectKey = "objects/photo.jpg",
                Reason = "Possible sexual content (score 0.700).",
                CreatedBy = UserId,
                OwnerType = "Customer",
                OwnerId = CustomerId,
                FolderType = "ClientForms",
                OccurredOn = Now,
            },
            repository,
            verdicts,
            new FixedCustomerReader(),
            new RecordingUnitOfWork(),
            bus,
            new TestCorrelationContext(),
            NullLogger<FormRequest>.Instance,
            new RecordingClientFormsMetrics(),
            default
        );

        Assert.Single(bus.Published.OfType<FormAttachmentRejectedIntegrationEvent>());
        Assert.Empty(bus.Published.OfType<FileSourceDeclaredIntegrationEvent>());
    }

    private static Task AvailableAsync(
        FormRequest request,
        Guid fileId,
        FakeMessageBus bus,
        MemoryFormFileScanVerdictRepository verdicts
    ) =>
        FormFileScanResultConsumer.Handle(
            new FileAvailableIntegrationEvent
            {
                TenantId = TenantId,
                FileId = fileId,
                ObjectKey = "objects/return.pdf",
                ContentType = "application/pdf",
                SizeBytes = 10,
                ChecksumSha256 = new string('a', 64),
                CreatedBy = UserId,
                OwnerType = "Customer",
                OwnerId = CustomerId,
                FolderType = "ClientForms",
                OriginalName = "return.pdf",
                OccurredOn = Now,
            },
            new SingleFormRequestRepository(request),
            verdicts,
            new FixedCustomerReader(),
            new RecordingUnitOfWork(),
            bus,
            new TestCorrelationContext(),
            NullLogger<FormRequest>.Instance,
            new RecordingClientFormsMetrics(),
            default
        );
}
