using BuildingBlocks.Contracts.Files;
using Microsoft.Extensions.Options;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Application.Notes.Queries;

/// <summary>Archivos de notas que el actor vería en Notes (mismas reglas que el feed); lo demás se omite.</summary>
public sealed record GetVisibleNoteFilesQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool ActorHasViewAll,
    bool CanViewAllCustomers,
    IReadOnlyList<Guid> FileIds
);

public static class GetVisibleNoteFilesHandler
{
    public static async Task<IReadOnlyList<VisibleFile>> Handle(
        GetVisibleNoteFilesQuery query,
        INoteRepository notes,
        IOptions<NotesVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        if (query.FileIds.Count == 0)
            return [];

        var assignedTo = visibility.Value.Enabled && !query.CanViewAllCustomers ? query.ActorUserId : (Guid?)null;
        var rows = await notes.ListVisibleAttachmentFilesAsync(
            query.TenantId,
            query.FileIds,
            query.ActorUserId,
            query.ActorHasViewAll,
            assignedTo,
            ct
        );

        // Un archivo en varias notas: primero una que ve todo el personal, después la del adjunto más reciente.
        return
        [
            .. rows.GroupBy(row => row.FileId)
                .Select(group =>
                    group
                        .OrderBy(row => RestrictionOf(row, query.ActorUserId) is null ? 0 : 1)
                        .ThenByDescending(row => row.LinkedAtUtc)
                        .ThenBy(row => row.NoteId)
                        .First()
                )
                .Select(row => ToVisibleFile(row, query.ActorUserId)),
        ];
    }

    private static VisibleFile ToVisibleFile(NoteAttachmentFileRow row, Guid actorUserId) =>
        new(
            row.FileId,
            FileVisibilityResourceTypes.Note,
            row.NoteId,
            GetNoteWorkTitlesHandler.TitleOf(row.Text),
            NoteFileSources.CustomerOf(row.TargetType, row.TargetId),
            RestrictedTo: RestrictionOf(row, actorUserId)
        );

    // Privada: la ve su autor, o quien tiene notes.view_all sin ser el autor.
    private static string? RestrictionOf(NoteAttachmentFileRow row, Guid actorUserId) =>
        row.Visibility != NoteVisibility.Private ? null
        : row.AuthorUserId == actorUserId ? FileVisibilityRestrictions.OnlyYou
        : FileVisibilityRestrictions.AdminView;
}
