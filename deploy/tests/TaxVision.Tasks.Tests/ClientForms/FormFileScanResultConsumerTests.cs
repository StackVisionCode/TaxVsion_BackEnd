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

    // T-20: retenido por la moderación queda "en revisión" (no vence ni se rechaza) y se aprueba después.
    [Fact]
    public async Task Pending_review_holds_the_attachment_until_the_final_verdict()
    {
        var fileId = Guid.NewGuid();
        var request = CustomerRequest();
        RegisterFile(request, fileId);
        var repository = new SingleFormRequestRepository(request);
        var verdicts = new MemoryFormFileScanVerdictRepository();

        await FormFileScanResultConsumer.Handle(
            new FilePendingReviewIntegrationEvent
            {
                TenantId = TenantId,
                FileId = fileId,
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
            null!,
            new RecordingUnitOfWork(),
            new FakeMessageBus(),
            new TestCorrelationContext(),
            NullLogger<FormRequest>.Instance,
            new RecordingClientFormsMetrics(),
            default
        );

        var attachment = Assert.Single(request.Submission!.Attachments);
        Assert.Equal(FormAttachmentStatus.UnderReview, attachment.Status);
        Assert.Equal(0, request.ExpireStalePendingAttachments(Now.AddDays(30), Now.AddDays(30)));

        await FormFileScanResultConsumer.Handle(
            new FileAvailableIntegrationEvent
            {
                TenantId = TenantId,
                FileId = fileId,
                ObjectKey = "objects/photo.jpg",
                ContentType = "image/jpeg",
                SizeBytes = 10,
                ChecksumSha256 = new string('a', 64),
                CreatedBy = UserId,
                OwnerType = "Customer",
                OwnerId = CustomerId,
                FolderType = "ClientForms",
                OriginalName = "photo.jpg",
                OccurredOn = Now.AddHours(2),
            },
            repository,
            verdicts,
            null!,
            new RecordingUnitOfWork(),
            new FakeMessageBus(),
            new TestCorrelationContext(),
            NullLogger<FormRequest>.Instance,
            new RecordingClientFormsMetrics(),
            default
        );

        Assert.Equal(FormAttachmentStatus.Available, attachment.Status);
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
