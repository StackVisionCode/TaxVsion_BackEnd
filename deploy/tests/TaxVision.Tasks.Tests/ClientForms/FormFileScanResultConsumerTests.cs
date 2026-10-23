using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Consumers;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Tests.Dependencies;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormFileScanResultConsumerTests
{
    [Fact]
    public async Task A_request_of_another_tenant_is_skipped_without_blocking_the_own_one()
    {
        var fileId = Guid.NewGuid();
        var own = CustomerRequest();
        var foreign = CustomerRequest(tenantId: Guid.NewGuid());
        RegisterFile(own, fileId);
        RegisterFile(foreign, fileId);
        var unitOfWork = new RecordingUnitOfWork();

        await FormFileScanResultConsumer.Handle(
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
            new SingleFormRequestRepository(own, foreign),
            new MemoryFormFileScanVerdictRepository(),
            null!,
            unitOfWork,
            new FakeMessageBus(),
            new TestCorrelationContext(),
            NullLogger<FormRequest>.Instance,
            new RecordingClientFormsMetrics(),
            default
        );

        Assert.Equal(FormAttachmentStatus.Available, Assert.Single(own.Submission!.Attachments).Status);
        Assert.Equal(FormAttachmentStatus.Pending, Assert.Single(foreign.Submission!.Attachments).Status);
        Assert.Equal(1, unitOfWork.Saves);
    }

    private static void RegisterFile(FormRequest request, Guid fileId) =>
        Assert.True(
            request
                .RegisterAttachment(
                    new FormAnswerAddress(FileComponent, null),
                    fileId,
                    "return.pdf",
                    "application/pdf",
                    10,
                    2,
                    20,
                    null,
                    Now
                )
                .IsSuccess
        );
}
