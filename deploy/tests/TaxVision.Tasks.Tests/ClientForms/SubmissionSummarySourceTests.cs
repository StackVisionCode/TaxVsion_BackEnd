using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Actions.Consumers;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Tests.Dependencies;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.ClientForms;

/// <summary>Enlazar el PDF resumen de una revisión lo declara como <c>summary_pdf</c> del request, después de guardar.</summary>
public sealed class SubmissionSummarySourceTests
{
    [Fact]
    public async Task Linking_the_summary_declares_it_after_saving()
    {
        var (request, generationId) = RequestAwaitingSummary();
        var fileId = Guid.NewGuid();
        var bus = new FakeMessageBus();
        var unitOfWork = new SnapshotUnitOfWork(bus);

        await HandleAsync(request, Completed(generationId, fileId, "client_form.submission_summary"), bus, unitOfWork);

        Assert.Equal(0, unitOfWork.PublishedAtSave);
        var declared = Assert.Single(bus.Published.OfType<FileSourceDeclaredIntegrationEvent>());
        Assert.Equal(
            (fileId, "form_request", request.Id, "summary_pdf", (Guid?)CustomerId),
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
    public async Task Another_document_type_declares_nothing()
    {
        var (request, generationId) = RequestAwaitingSummary();
        var bus = new FakeMessageBus();
        var unitOfWork = new SnapshotUnitOfWork(bus);

        await HandleAsync(request, Completed(generationId, Guid.NewGuid(), "invoice"), bus, unitOfWork);

        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.Saves);
    }

    private static (FormRequest Request, Guid GenerationId) RequestAwaitingSummary()
    {
        var request = CustomerRequest();
        var revision = Submit(request);
        var generationId = Guid.NewGuid();
        Assert.True(request.Summary.MarkPdfRequested(revision.RevisionNumber, generationId).IsSuccess);
        return (request, generationId);
    }

    private static DocumentGenerationCompletedIntegrationEvent Completed(
        Guid generationId,
        Guid fileId,
        string documentType
    ) =>
        new()
        {
            TenantId = TenantId,
            GenerationId = Guid.NewGuid(),
            ExternalReferenceId = generationId,
            DocumentType = documentType,
            OwnerType = "Customer",
            OwnerId = CustomerId,
            DocumentVersion = 1,
            FileId = fileId,
            FileName = "summary.pdf",
            ContentType = "application/pdf",
            SizeBytes = 10,
        };

    private static Task HandleAsync(
        FormRequest request,
        DocumentGenerationCompletedIntegrationEvent evt,
        FakeMessageBus bus,
        SnapshotUnitOfWork unitOfWork
    ) =>
        SubmissionSummaryGenerationCompletedConsumer.Handle(
            evt,
            new SingleFormRequestRepository(request),
            new ClientFormSignatureDispatcher(
                null!,
                null!,
                null!,
                new TestCorrelationContext(),
                NullLogger<ClientFormSignatureDispatcher>.Instance
            ),
            unitOfWork,
            bus,
            new TestCorrelationContext(),
            NullLogger<DocumentGenerationCompletedIntegrationEvent>.Instance,
            default
        );

    // Anota cuántos mensajes había publicados en el momento de guardar.
    private sealed class SnapshotUnitOfWork(FakeMessageBus bus) : BuildingBlocks.Persistence.IUnitOfWork
    {
        public int Saves { get; private set; }
        public int PublishedAtSave { get; private set; } = -1;

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            PublishedAtSave = bus.Published.Count;
            return Task.FromResult(0);
        }
    }
}
