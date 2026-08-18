using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Domain;

public sealed class TaskAttachmentTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// El enlazado nace disponible. Si pasara por <c>Pending</c> esperaría un <c>FileAvailable</c>
    /// que CloudStorage publicó hace semanas y no vuelve a publicar: colgado para siempre.
    /// </summary>
    [Fact]
    public void Linking_an_existing_file_skips_pending()
    {
        var task = NewTask();

        var linked = task.LinkExistingFile(Guid.NewGuid(), "W-2 2025.pdf", "application/pdf", 1024, UserId, Now);

        Assert.True(linked.IsSuccess);
        Assert.Equal(AttachmentStatus.Available, linked.Value.Status);
        Assert.Equal(AttachmentOrigin.Linked, linked.Value.Origin);
    }

    [Fact]
    public void Uploading_a_new_file_waits_for_the_scan()
    {
        var task = NewTask();

        var uploaded = task.AttachUploadedFile(Guid.NewGuid(), "1099.pdf", "application/pdf", 512, UserId, Now);

        Assert.Equal(AttachmentStatus.Pending, uploaded.Value.Status);
    }

    [Fact]
    public void The_same_file_cannot_be_attached_twice_while_active()
    {
        var task = NewTask();
        var fileId = Guid.NewGuid();
        task.LinkExistingFile(fileId, "W-2.pdf", null, 10, UserId, Now);

        var second = task.LinkExistingFile(fileId, "W-2 otra vez.pdf", null, 10, UserId, Now);

        Assert.Equal(TaskErrors.Attachment.Duplicate, second.Error);
    }

    [Fact]
    public void After_detaching_the_same_file_can_be_attached_again()
    {
        var task = NewTask();
        var fileId = Guid.NewGuid();
        task.LinkExistingFile(fileId, "W-2.pdf", null, 10, UserId, Now);
        task.DetachFile(fileId, Now);

        var again = task.LinkExistingFile(fileId, "W-2.pdf", null, 10, UserId, Now);

        Assert.True(again.IsSuccess);
    }

    [Fact]
    public void The_twenty_first_active_attachment_is_rejected()
    {
        var task = NewTask();
        for (var i = 0; i < TaskItem.MaxActiveAttachments; i++)
            task.LinkExistingFile(Guid.NewGuid(), $"doc-{i}.pdf", null, 10, UserId, Now);

        var extra = task.LinkExistingFile(Guid.NewGuid(), "doc-21.pdf", null, 10, UserId, Now);

        Assert.Equal(TaskErrors.Attachment.LimitReached, extra.Error);
    }

    /// <summary>Los detached no cuentan para el tope: el hueco se libera al desadjuntar.</summary>
    [Fact]
    public void Detached_attachments_do_not_count_toward_the_limit()
    {
        var task = NewTask();
        var first = Guid.NewGuid();
        task.LinkExistingFile(first, "doc-0.pdf", null, 10, UserId, Now);
        for (var i = 1; i < TaskItem.MaxActiveAttachments; i++)
            task.LinkExistingFile(Guid.NewGuid(), $"doc-{i}.pdf", null, 10, UserId, Now);

        task.DetachFile(first, Now);
        var extra = task.LinkExistingFile(Guid.NewGuid(), "doc-nuevo.pdf", null, 10, UserId, Now);

        Assert.True(extra.IsSuccess);
    }

    [Fact]
    public void A_closed_task_takes_no_new_attachments()
    {
        var task = NewTask();
        task.Complete(UserId, Now);

        var attached = task.LinkExistingFile(Guid.NewGuid(), "tarde.pdf", null, 10, UserId, Now);

        Assert.Equal(TaskErrors.Attachment.TaskClosed, attached.Error);
    }

    /// <summary>
    /// El escaneo no bloquea el trabajo: el preparador cierra la tarea y, si el archivo resulta
    /// rechazado después, el aviso lo alcanza igual.
    /// </summary>
    [Fact]
    public void A_pending_attachment_does_not_block_completing_the_task()
    {
        var task = NewTask();
        task.AttachUploadedFile(Guid.NewGuid(), "escaneando.pdf", null, 10, UserId, Now);

        var completed = task.Complete(UserId, Now);

        Assert.True(completed.IsSuccess);
    }

    /// <summary>
    /// El consumer recibe los archivos de todo el monorepo. Un id ajeno tiene que salir callado, no
    /// reventar: la DLQ se llenaría de eventos de Notes y Signature.
    /// </summary>
    [Fact]
    public void Marking_an_unknown_file_is_a_silent_no_op()
    {
        var task = NewTask();

        Assert.False(task.MarkAttachmentAvailable(Guid.NewGuid()));
        Assert.False(task.MarkAttachmentRejected(Guid.NewGuid(), "infected", Now));
        Assert.False(task.MarkAttachmentDetached(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Marking_available_twice_is_idempotent()
    {
        var task = NewTask();
        var fileId = Guid.NewGuid();
        task.AttachUploadedFile(fileId, "doc.pdf", null, 10, UserId, Now);

        Assert.True(task.MarkAttachmentAvailable(fileId));
        Assert.False(task.MarkAttachmentAvailable(fileId));
    }

    [Fact]
    public void Rejecting_records_the_reason_and_raises_the_event()
    {
        var task = NewTask();
        var fileId = Guid.NewGuid();
        task.AttachUploadedFile(fileId, "eicar.txt", null, 68, UserId, Now);

        var rejected = task.MarkAttachmentRejected(fileId, "infected", Now);

        Assert.True(rejected);
        Assert.Equal("infected", task.Attachments[0].RejectionReason);
        Assert.Contains(task.DomainEvents, e => e.GetType().Name == "TaskAttachmentRejectedDomainEvent");
    }

    private static TaskItem NewTask() =>
        TaskItem
            .Create(
                Guid.NewGuid(),
                UserId,
                TaskTitle.Create("Revisar documentos").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                UserId,
                Now
            )
            .Value;
}
