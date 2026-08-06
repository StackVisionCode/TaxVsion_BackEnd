using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Notes.Domain.ValueObjects;

namespace TaxVision.Notes.Domain.Notes;

/// <summary>
/// Aggregate root de una nota. Estados: <c>Active ⇄ Archived</c> (reversible), <c>→ Deleted</c>
/// (terminal, soft, oculto salvo governance). Sin <c>ChangeStatus(x)</c> genérico — cada transición
/// tiene su propio método explícito con su regla concreta (guardrail 2).
///
/// <para>
/// El aggregate NO conoce roles ni permisos (guardrail 4: dominio puro). La regla "solo el autor
/// edita el contenido; el admin con <c>notes.view_all</c> solo lee/archiva" se aplica en la capa
/// Application/Api (chequeo de autor + Capa 4b de authz), no aquí. Los parámetros
/// <c>editorUserId</c>/<c>actorUserId</c> solo documentan quién invocó la transición — no
/// autorizan nada — y quedan disponibles para enriquecer los domain events cuando Fase 5 conecte
/// la publicación de integración (01_Modelo_De_Dominio.md §5).
/// </para>
/// </summary>
public sealed class Note : TenantEntity, IHasOwner
{
    public const int MaxAttachmentsPerNote = 20;

    private readonly List<NoteAttachment> _attachments = [];

    private Note() { }

    public Guid CreatedByUserId { get; private set; }
    public NoteContent Content { get; private set; } = default!;
    public NoteReference Reference { get; private set; } = default!;
    public NoteVisibility Visibility { get; private set; }
    public NoteColor? Color { get; private set; }
    public bool IsPinned { get; private set; }
    public NoteStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<NoteAttachment> Attachments => _attachments.AsReadOnly();

    // ------------------------------------------------------------------
    // Factory
    // ------------------------------------------------------------------

    public static Result<Note> Create(
        Guid tenantId,
        Guid authorUserId,
        NoteContent content,
        NoteReference reference,
        NoteVisibility visibility,
        NoteColor? color
    )
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(reference);

        if (tenantId == Guid.Empty)
            return Result.Failure<Note>(new Error("Note.Tenant", "TenantId is required."));
        if (authorUserId == Guid.Empty)
            return Result.Failure<Note>(new Error("Note.CreatedBy", "CreatedByUserId is required."));

        var now = DateTime.UtcNow;
        var note = new Note
        {
            Id = Guid.NewGuid(),
            CreatedByUserId = authorUserId,
            Content = content,
            Reference = reference,
            Visibility = visibility,
            Color = color,
            IsPinned = false,
            Status = NoteStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        note.SetTenant(tenantId);
        return Result.Success(note);
    }

    // ------------------------------------------------------------------
    // Mutación de contenido/metadata — cada una con SU regla explícita
    // ------------------------------------------------------------------

    public Result UpdateContent(NoteContent newContent, Guid editorUserId)
    {
        ArgumentNullException.ThrowIfNull(newContent);

        var guard = EnsureNotDeleted();
        if (guard.IsFailure)
            return guard;

        Content = newContent;
        Touch(editorUserId);
        return Result.Success();
    }

    public Result ChangeVisibility(NoteVisibility newVisibility, Guid editorUserId)
    {
        var guard = EnsureNotDeleted();
        if (guard.IsFailure)
            return guard;

        Visibility = newVisibility;
        Touch(editorUserId);
        return Result.Success();
    }

    public Result Pin(Guid editorUserId)
    {
        var guard = EnsureNotDeleted();
        if (guard.IsFailure)
            return guard;

        IsPinned = true;
        Touch(editorUserId);
        return Result.Success();
    }

    public Result Unpin(Guid editorUserId)
    {
        var guard = EnsureNotDeleted();
        if (guard.IsFailure)
            return guard;

        IsPinned = false;
        Touch(editorUserId);
        return Result.Success();
    }

    public Result SetColor(NoteColor? color, Guid editorUserId)
    {
        var guard = EnsureNotDeleted();
        if (guard.IsFailure)
            return guard;

        Color = color;
        Touch(editorUserId);
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Adjuntos — Caso B (ADR-07): CloudStorage ya validó el archivo antes de que
    // el frontend nos entregue el fileId; los consumers mueven Pending → Available/Rejected.
    // ------------------------------------------------------------------

    public Result AttachFile(Guid cloudStorageFileId, string displayName, string contentType, long sizeBytes)
    {
        var guard = EnsureNotDeleted();
        if (guard.IsFailure)
            return guard;

        if (cloudStorageFileId == Guid.Empty)
            return Result.Failure(new Error("Note.AttachmentFile", "CloudStorageFileId is required."));
        if (string.IsNullOrWhiteSpace(displayName))
            return Result.Failure(new Error("Note.AttachmentDisplayName", "DisplayName is required."));
        if (string.IsNullOrWhiteSpace(contentType))
            return Result.Failure(new Error("Note.AttachmentContentType", "ContentType is required."));
        if (sizeBytes <= 0)
            return Result.Failure(new Error("Note.AttachmentSize", "SizeBytes must be positive."));

        if (_attachments.Any(a => a.CloudStorageFileId == cloudStorageFileId))
            return Result.Failure(NoteErrors.AttachmentDuplicate);

        if (_attachments.Count >= MaxAttachmentsPerNote)
            return Result.Failure(NoteErrors.AttachmentLimit);

        var attachment = NoteAttachment.Create(
            Id,
            cloudStorageFileId,
            displayName.Trim(),
            contentType.Trim(),
            sizeBytes,
            DateTime.UtcNow
        );
        _attachments.Add(attachment);
        Touch();
        return Result.Success();
    }

    /// <summary>Idempotente. Invocado por el consumer de <c>FileAvailableIntegrationEvent</c>.</summary>
    public Result MarkAttachmentAvailable(Guid cloudStorageFileId)
    {
        var attachment = FindAttachmentOrNull(cloudStorageFileId);
        if (attachment is null)
            return Result.Failure(NoteErrors.AttachmentNotFound);

        attachment.MarkAvailable();
        Touch();
        return Result.Success();
    }

    /// <summary>Idempotente. Invocado por los consumers de infected/blocked-by-policy.</summary>
    public Result MarkAttachmentRejected(Guid cloudStorageFileId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("Note.AttachmentRejectionReason", "Reason is required."));

        var attachment = FindAttachmentOrNull(cloudStorageFileId);
        if (attachment is null)
            return Result.Failure(NoteErrors.AttachmentNotFound);

        attachment.MarkRejected(reason.Trim());
        Touch();
        return Result.Success();
    }

    /// <summary>Soft: mueve el adjunto a <see cref="NoteAttachmentStatus.Detached"/>, no lo remueve de la colección.</summary>
    public Result DetachFile(Guid cloudStorageFileId)
    {
        var guard = EnsureNotDeleted();
        if (guard.IsFailure)
            return guard;

        var attachment = FindAttachmentOrNull(cloudStorageFileId);
        if (attachment is null)
            return Result.Failure(NoteErrors.AttachmentNotFound);

        attachment.MarkDetached();
        Touch();
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Ciclo de vida
    // ------------------------------------------------------------------

    public Result Archive(Guid actorUserId)
    {
        if (Status == NoteStatus.Deleted)
            return Result.Failure(NoteErrors.Deleted);
        if (Status == NoteStatus.Archived)
            return Result.Failure(NoteErrors.InvalidTransition);

        Status = NoteStatus.Archived;
        Touch(actorUserId);
        return Result.Success();
    }

    public Result Restore(Guid actorUserId)
    {
        if (Status == NoteStatus.Deleted)
            return Result.Failure(NoteErrors.Deleted);
        if (Status == NoteStatus.Active)
            return Result.Failure(NoteErrors.InvalidTransition);

        Status = NoteStatus.Active;
        Touch(actorUserId);
        return Result.Success();
    }

    public Result SoftDelete(Guid actorUserId)
    {
        if (Status == NoteStatus.Deleted)
            return Result.Failure(NoteErrors.Deleted);

        Status = NoteStatus.Deleted;
        Touch(actorUserId);
        return Result.Success();
    }

    // ==================================================================
    // Helpers privados
    // ==================================================================

    private Result EnsureNotDeleted() =>
        Status == NoteStatus.Deleted ? Result.Failure(NoteErrors.Deleted) : Result.Success();

    private NoteAttachment? FindAttachmentOrNull(Guid cloudStorageFileId) =>
        _attachments.Find(a => a.CloudStorageFileId == cloudStorageFileId);

    /// <summary><paramref name="editorUserId"/> no se persiste todavía (Fase 5 lo usará para enriquecer domain events).</summary>
    private void Touch(Guid editorUserId)
    {
        _ = editorUserId;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
