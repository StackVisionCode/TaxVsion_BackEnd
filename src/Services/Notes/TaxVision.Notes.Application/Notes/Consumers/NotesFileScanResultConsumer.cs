using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.NotesIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Domain.Notes;
using Wolverine;

namespace TaxVision.Notes.Application.Notes.Consumers;

// ---------------------------------------------------------------------------
// 03_Plan_De_Fases.md §Fase 7 (02_Contratos §4) — Caso B: CloudStorage ya validó/movió el
// archivo antes de publicar; Notes solo reacciona, CERO MinIO/M2M propio. Cada handler:
// correlation.Push + INoteRepository.GetByAttachmentFileIdAsync (sin tenant ambiental, guardrail 8
// — el consumer de Wolverine no corre dentro de un scope HTTP con tenant seteado) + guard de
// tenant explícito post-fetch (nunca confiar en evt.TenantId sin comparar contra el dueño real del
// adjunto) + idempotencia (los métodos de NoteAttachment ya son no-op en su estado final, Fase 1).
// ---------------------------------------------------------------------------

public static class NotesFileScanResultConsumer
{
    public static async Task Handle(
        FileAvailableIntegrationEvent evt,
        INoteRepository notes,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<Note> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var note = await LoadOwningNoteOrNullAsync(evt.TenantId, evt.FileId, notes, logger, ct);
            if (note is null)
                return;

            var result = note.MarkAttachmentAvailable(evt.FileId);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "MarkAttachmentAvailable failed for file {FileId} on note {NoteId}: {ErrorCode}",
                    evt.FileId,
                    note.Id,
                    result.Error.Code
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    public static async Task Handle(
        FileInfectedDetectedIntegrationEvent evt,
        INoteRepository notes,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<Note> logger,
        CancellationToken ct
    ) =>
        await RejectAttachmentAsync(
            evt.TenantId,
            evt.FileId,
            "infected",
            notes,
            unitOfWork,
            correlation,
            evt.CorrelationId,
            evt.EventId,
            logger,
            ct
        );

    public static async Task Handle(
        FileBlockedByPolicyIntegrationEvent evt,
        INoteRepository notes,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<Note> logger,
        CancellationToken ct
    ) =>
        await RejectAttachmentAsync(
            evt.TenantId,
            evt.FileId,
            "blocked-by-policy",
            notes,
            unitOfWork,
            correlation,
            evt.CorrelationId,
            evt.EventId,
            logger,
            ct
        );

    /// <summary>
    /// Si CloudStorage borra el objeto por debajo (papelera/retención), el adjunto acá ya no tiene
    /// bytes reales — se mueve a <c>Detached</c> (mismo estado final que el detach manual del
    /// usuario) y se publica el mismo evento de integración para que MyPlanner/consumidores
    /// externos se enteren igual, venga de donde venga la desvinculación.
    /// </summary>
    public static async Task Handle(
        FileDeletedIntegrationEvent evt,
        INoteRepository notes,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<Note> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var note = await LoadOwningNoteOrNullAsync(evt.TenantId, evt.FileId, notes, logger, ct);
            if (note is null)
                return;

            var result = note.DetachFile(evt.FileId);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "DetachFile (reactive, FileDeleted) failed for file {FileId} on note {NoteId}: {ErrorCode}",
                    evt.FileId,
                    note.Id,
                    result.Error.Code
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
            await bus.PublishAsync(
                new NoteAttachmentDetachedIntegrationEvent
                {
                    TenantId = note.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    NoteId = note.Id,
                    CloudStorageFileId = evt.FileId,
                }
            );
        }
    }

    private static async Task RejectAttachmentAsync(
        Guid tenantId,
        Guid fileId,
        string reason,
        INoteRepository notes,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        string eventCorrelationId,
        Guid eventId,
        ILogger<Note> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(string.IsNullOrWhiteSpace(eventCorrelationId) ? eventId.ToString("N") : eventCorrelationId)
        )
        {
            var note = await LoadOwningNoteOrNullAsync(tenantId, fileId, notes, logger, ct);
            if (note is null)
                return;

            var result = note.MarkAttachmentRejected(fileId, reason);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "MarkAttachmentRejected failed for file {FileId} on note {NoteId}: {ErrorCode}",
                    fileId,
                    note.Id,
                    result.Error.Code
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Guardrail 8 — <see cref="INoteRepository.GetByAttachmentFileIdAsync"/> no filtra por tenant
    /// (el consumer no tiene tenant ambiental). Compara <paramref name="tenantId"/> del evento
    /// contra el dueño real de la nota antes de mutar nada; nunca confiar ciegamente en el evento.
    /// </summary>
    private static async Task<Note?> LoadOwningNoteOrNullAsync(
        Guid tenantId,
        Guid fileId,
        INoteRepository notes,
        ILogger<Note> logger,
        CancellationToken ct
    )
    {
        var note = await notes.GetByAttachmentFileIdAsync(fileId, ct);
        if (note is null)
            return null; // adjunto de otro servicio (Signature/Correspondence/etc.), no de Notes.

        if (note.TenantId != tenantId)
        {
            logger.LogWarning(
                "CloudStorage file scan event for file {FileId} carries tenant {EventTenantId} but the owning note belongs to tenant {NoteTenantId} — ignoring.",
                fileId,
                tenantId,
                note.TenantId
            );
            return null;
        }

        return note;
    }
}
