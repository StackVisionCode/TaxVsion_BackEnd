using BuildingBlocks.Common;
using BuildingBlocks.Messaging.NotesIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Application.Projections.Abstractions;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Notes.Application.Notes.Commands;

public sealed record UpdateNoteContentCommand(
    Guid TenantId,
    Guid NoteId,
    Guid ActorUserId,
    string RawHtml,
    bool ActorHasViewAll
);

/// <summary>El autor edita el contenido (03_Plan_De_Fases.md §Fase 5); desde el punto 3.2 también un staff con <c>notes.view_all</c> si el autor fue retirado, ver <see cref="NoteVisibilityPolicy.CanEditContentAsync"/>.</summary>
public static class UpdateNoteContentHandler
{
    public static async Task<Result<NoteResponse>> Handle(
        UpdateNoteContentCommand command,
        INoteRepository notes,
        IHtmlSanitizer sanitizer,
        IOffboardedStaffRepository offboardedStaff,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var note = await notes.GetByIdAsync(command.TenantId, command.NoteId, ct);
        if (note is null)
            return Result.Failure<NoteResponse>(NoteErrors.NotFound);

        if (
            !await NoteVisibilityPolicy.CanEditContentAsync(
                note,
                command.ActorUserId,
                command.ActorHasViewAll,
                offboardedStaff,
                ct
            )
        )
            return Result.Failure<NoteResponse>(NoteErrors.Forbidden);

        var contentResult = NoteContent.Create(sanitizer.Sanitize(command.RawHtml));
        if (contentResult.IsFailure)
            return Result.Failure<NoteResponse>(contentResult.Error);

        var updateResult = note.UpdateContent(contentResult.Value, command.ActorUserId);
        if (updateResult.IsFailure)
            return Result.Failure<NoteResponse>(updateResult.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await PublishUpdatedAsync(note, correlation, bus);

        return Result.Success(NoteResponse.From(note));
    }

    internal static ValueTask PublishUpdatedAsync(Note note, ICorrelationContext correlation, IMessageBus bus) =>
        bus.PublishAsync(
            new NoteUpdatedIntegrationEvent
            {
                TenantId = note.TenantId,
                CorrelationId = correlation.CorrelationId,
                NoteId = note.Id,
                Visibility = note.Visibility.ToString(),
                IsPinned = note.IsPinned,
                UpdatedAtUtc = note.UpdatedAtUtc,
            }
        );
}
