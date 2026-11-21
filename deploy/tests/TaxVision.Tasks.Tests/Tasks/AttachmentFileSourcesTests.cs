using TaxVision.Tasks.Application.Attachments;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>Solo el adjunto subido a la tarea se declara a CloudStorage; enlazados y de plantilla conservan su origen.</summary>
public sealed class AttachmentFileSourcesTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_uploaded_attachment_declares_the_task_and_its_customer()
    {
        var task = NewTask(TaskReference.Create(CustomerId, 2025).Value);
        var attachment = task.AttachUploadedFile(Guid.NewGuid(), "W-2.pdf", "application/pdf", 10, Owner, Now).Value;

        var declared = AttachmentFileSources.ForUploaded(task, attachment, "corr-1");

        Assert.NotNull(declared);
        Assert.Equal(
            (TenantId, attachment.FileId, "tasks", "task", task.Id, "attachment", (Guid?)CustomerId, "corr-1"),
            (
                declared.TenantId,
                declared.FileId,
                declared.SourceService,
                declared.SourceResourceType,
                declared.SourceResourceId,
                declared.SourceRole,
                declared.RelatedCustomerId,
                declared.CorrelationId
            )
        );
    }

    [Fact]
    public void A_task_without_customer_declares_no_related_customer()
    {
        var task = NewTask(TaskReference.None);
        var attachment = task.AttachUploadedFile(Guid.NewGuid(), "notes.pdf", "application/pdf", 10, Owner, Now).Value;

        var declared = AttachmentFileSources.ForUploaded(task, attachment, "corr-1");

        Assert.NotNull(declared);
        Assert.Null(declared.RelatedCustomerId);
    }

    [Fact]
    public void Linked_and_template_attachments_are_not_declared()
    {
        var task = NewTask(TaskReference.Create(CustomerId, 2025).Value);
        var linked = task.LinkExistingFile(Guid.NewGuid(), "W-2.pdf", "application/pdf", 10, Owner, Now).Value;
        var template = task.AttachTemplateFile(Guid.NewGuid(), "guide.pdf", "application/pdf", 10, Owner, Now).Value;

        Assert.Null(AttachmentFileSources.ForUploaded(task, linked, "corr-1"));
        Assert.Null(AttachmentFileSources.ForUploaded(task, template, "corr-1"));
    }

    private static TaskItem NewTask(TaskReference reference) =>
        TaskItem
            .Create(
                TenantId,
                Owner,
                TaskTitle.Create("Prepare return").Value,
                null,
                TaskPriority.Normal,
                reference,
                null,
                null,
                Owner,
                Now
            )
            .Value;
}
