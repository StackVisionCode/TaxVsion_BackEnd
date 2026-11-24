using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Domain.Tasks.Events;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Domain.Tasks;

/// <summary>Archivos de la tarea: vincular, subir, revisar y quitar.</summary>
public sealed partial class TaskItem
{
    /// <summary>
    /// Enlaza un archivo que ya está en CloudStorage —el caso dominante: el W-2 lo subió el cliente
    /// por el portal antes de que existiera la tarea—.
    /// </summary>
    public Result<TaskAttachment> LinkExistingFile(
        Guid fileId,
        string? displayName,
        string? contentType,
        long sizeBytes,
        Guid byUserId,
        DateTime nowUtc
    )
    {
        var allowed = EnsureCanAttach(fileId, displayName);
        if (allowed.IsFailure)
            return Result.Failure<TaskAttachment>(allowed.Error);

        var attachment = TaskAttachment.Link(
            Id,
            TenantId,
            fileId,
            allowed.Value,
            contentType,
            sizeBytes,
            byUserId,
            nowUtc
        );
        _attachments.Add(attachment);
        return Result.Success(attachment);
    }

    /// <summary>Recién subido a CloudStorage: queda pendiente hasta que el escaneo se pronuncie.</summary>
    public Result<TaskAttachment> AttachUploadedFile(
        Guid fileId,
        string? displayName,
        string? contentType,
        long sizeBytes,
        Guid byUserId,
        DateTime nowUtc
    )
    {
        var allowed = EnsureCanAttach(fileId, displayName);
        if (allowed.IsFailure)
            return Result.Failure<TaskAttachment>(allowed.Error);

        var attachment = TaskAttachment.Upload(
            Id,
            TenantId,
            fileId,
            allowed.Value,
            contentType,
            sizeBytes,
            byUserId,
            nowUtc
        );
        _attachments.Add(attachment);
        return Result.Success(attachment);
    }

    /// <summary>
    /// El archivo de referencia del guion. Nace <c>Available</c> igual que un enlazado —ya está en
    /// CloudStorage y ya fue escaneado— y lo comparten todas las instancias de la plantilla: un solo
    /// objeto, N referencias.
    /// </summary>
    internal Result<TaskAttachment> AttachTemplateFile(
        Guid fileId,
        string displayName,
        string? contentType,
        long sizeBytes,
        Guid byUserId,
        DateTime nowUtc
    )
    {
        var allowed = EnsureCanAttach(fileId, displayName);
        if (allowed.IsFailure)
            return Result.Failure<TaskAttachment>(allowed.Error);

        var attachment = TaskAttachment.FromTemplate(
            Id,
            TenantId,
            fileId,
            allowed.Value,
            contentType,
            sizeBytes,
            byUserId,
            nowUtc
        );
        _attachments.Add(attachment);
        return Result.Success(attachment);
    }

    /// <summary>
    /// Los tres <c>MarkAttachment*</c> devuelven <c>false</c> cuando el archivo no es de esta tarea:
    /// el consumer recibe los eventos de todo el monorepo y tirar excepción llenaría la DLQ con
    /// archivos ajenos.
    /// </summary>
    public bool MarkAttachmentAvailable(Guid fileId) => FindActive(fileId)?.MarkAvailable() ?? false;

    public bool MarkAttachmentUnderReview(Guid fileId) => FindActive(fileId)?.MarkUnderReview() ?? false;

    public bool MarkAttachmentRejected(Guid fileId, string reason, DateTime nowUtc)
    {
        if (FindActive(fileId) is not { } attachment || !attachment.MarkRejected(reason, nowUtc))
            return false;

        AddDomainEvent(
            new TaskAttachmentRejectedDomainEvent(
                Id,
                TenantId,
                attachment.Id,
                fileId,
                attachment.DisplayName,
                reason,
                attachment.AttachedByUserId,
                nowUtc
            )
        );
        return true;
    }

    /// <summary>Lo borraron desde CloudStorage: la tarea no puede seguir mostrando el archivo.</summary>
    public bool MarkAttachmentDetached(Guid fileId, DateTime nowUtc) => FindActive(fileId)?.Detach(nowUtc) ?? false;

    /// <summary>
    /// El usuario lo quita de la tarea. El byte sigue en CloudStorage: Task no es dueño del archivo
    /// y borrarlo se llevaría por delante al resto de servicios que lo referencian.
    /// </summary>
    public Result DetachFile(Guid fileId, DateTime nowUtc)
    {
        if (FindActive(fileId) is not { } attachment)
            return Result.Failure(TaskErrors.Attachment.NotFound);

        attachment.Detach(nowUtc);
        return Result.Success();
    }

    private Result<string> EnsureCanAttach(Guid fileId, string? displayName)
    {
        if (IsClosed)
            return Result.Failure<string>(TaskErrors.Attachment.TaskClosed);

        if (fileId == Guid.Empty)
            return Result.Failure<string>(TaskErrors.Attachment.FileRequired);

        var trimmed = displayName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return Result.Failure<string>(TaskErrors.Attachment.DisplayNameRequired);

        if (trimmed.Length > 260)
            return Result.Failure<string>(TaskErrors.Attachment.DisplayNameTooLong);

        if (FindActive(fileId) is not null)
            return Result.Failure<string>(TaskErrors.Attachment.Duplicate);

        return _attachments.Count(a => a.IsActive) >= MaxActiveAttachments
            ? Result.Failure<string>(TaskErrors.Attachment.LimitReached)
            : Result.Success(trimmed);
    }

    private TaskAttachment? FindActive(Guid fileId) =>
        _attachments.FirstOrDefault(a => a.FileId == fileId && a.IsActive);
}
