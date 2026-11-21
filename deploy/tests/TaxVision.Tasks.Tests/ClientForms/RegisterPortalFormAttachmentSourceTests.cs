using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Tests.Dependencies;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.ClientForms;

/// <summary>
/// Si el escaneo ya había terminado cuando el portal registra el adjunto, nace disponible y no pasará por el consumer:
/// se declara aquí, después de guardar. Sin veredicto no se declara (lo hará el consumer).
/// </summary>
public sealed class RegisterPortalFormAttachmentSourceTests
{
    [Fact]
    public async Task A_file_already_scanned_is_declared_after_saving()
    {
        var version = Version();
        var request = CustomerRequest(version);
        var fileId = Guid.NewGuid();
        var bus = new FakeMessageBus();
        var unitOfWork = new SnapshotUnitOfWork(bus);

        var result = await RegisterAsync(request, version, fileId, KnownAvailable(fileId), bus, unitOfWork);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, unitOfWork.PublishedAtSave);
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
    public async Task A_file_still_pending_is_not_declared()
    {
        var version = Version();
        var request = CustomerRequest(version);
        var bus = new FakeMessageBus();

        var result = await RegisterAsync(
            request,
            version,
            Guid.NewGuid(),
            new MemoryFormFileScanVerdictRepository(),
            bus,
            new SnapshotUnitOfWork(bus)
        );

        Assert.True(result.IsSuccess);
        Assert.Empty(bus.Published);
    }

    private static MemoryFormFileScanVerdictRepository KnownAvailable(Guid fileId) =>
        new(
            FormFileScanVerdict.Create(
                TenantId,
                fileId,
                FormFileVerdictStatus.Available,
                "Customer",
                CustomerId,
                "ClientForms",
                "application/pdf",
                10,
                "return.pdf",
                Now,
                Now
            )
        );

    private static Task<BuildingBlocks.Results.Result<FormAttachmentResponse>> RegisterAsync(
        FormRequest request,
        TaxVision.Tasks.ClientForms.Domain.Versions.FormTemplateVersion version,
        Guid fileId,
        IFormFileScanVerdictRepository verdicts,
        FakeMessageBus bus,
        IUnitOfWork unitOfWork
    ) =>
        RegisterPortalFormAttachmentHandler.Handle(
            new RegisterPortalFormAttachmentCommand(
                Portal(request),
                request.Id,
                FileComponent,
                null,
                fileId,
                "return.pdf",
                "application/pdf",
                10,
                Now
            ),
            new SingleFormRequestRepository(request),
            new MemoryFormTemplateVersionRepository(version),
            verdicts,
            FormTemplateHandlerTestDoubles.Engine(),
            new PlainFormAnswersProtector(),
            new FormConsentGate(new MemoryConsentNoticeRepository(), new MemoryFormConsentRecordRepository()),
            unitOfWork,
            bus,
            new TestCorrelationContext(),
            default
        );

    // Anota cuántos mensajes había publicados en el momento de guardar.
    private sealed class SnapshotUnitOfWork(FakeMessageBus bus) : IUnitOfWork
    {
        public int PublishedAtSave { get; private set; } = -1;

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            PublishedAtSave = bus.Published.Count;
            return Task.FromResult(0);
        }
    }
}
