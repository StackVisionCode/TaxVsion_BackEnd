using BuildingBlocks.Common;
using BuildingBlocks.Messaging.NotesIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Application.Team.Consumers;

/// <summary>Notas creadas. Una nota privada queda privada en el historial (solo el autor o <c>notes.view_all</c>).</summary>
public static class NotesActivityConsumer
{
    public static Task Handle(
        NoteCreatedIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(evt.AuthorUserId, ActivityVerbs.NoteCreated, evt.NoteId, evt.CustomerId, VisibilityOf(evt.Visibility)),
            entries,
            unitOfWork,
            correlation,
            ct
        );

    // Solo Team y ClientVisible son de equipo; cualquier otro valor se trata como privado (fail-closed).
    private static ActivityVisibility VisibilityOf(string noteVisibility) =>
        string.Equals(noteVisibility, "Team", StringComparison.OrdinalIgnoreCase)
        || string.Equals(noteVisibility, "ClientVisible", StringComparison.OrdinalIgnoreCase)
            ? ActivityVisibility.Team
            : ActivityVisibility.Private;
}
