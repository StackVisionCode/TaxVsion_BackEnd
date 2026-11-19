using BuildingBlocks.Contracts.Work;
using Microsoft.Extensions.Options;
using TaxVision.Notes.Application.Notes.Abstractions;

namespace TaxVision.Notes.Application.Notes.Queries;

/// <summary>Títulos en vivo de las notas que el actor puede leer (mismas reglas que el feed).</summary>
public sealed record GetNoteWorkTitlesQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool ActorHasViewAll,
    bool CanViewAllCustomers,
    IReadOnlyList<Guid> Ids
);

public static class GetNoteWorkTitlesHandler
{
    public static async Task<WorkTitlesResponse> Handle(
        GetNoteWorkTitlesQuery query,
        INoteRepository notes,
        IOptions<NotesVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAllCustomers ? query.ActorUserId : (Guid?)null;
        var rows = await notes.ListTitlesAsync(
            query.TenantId,
            query.Ids,
            query.ActorUserId,
            query.ActorHasViewAll,
            assignedTo,
            ct
        );

        return new WorkTitlesResponse(
            rows.Select(row => (row.Id, Title: TitleOf(row.Text)))
                .Where(row => row.Title is not null)
                .Select(row => new WorkTitle(WorkResourceTypes.Note, row.Id, row.Title!))
                .ToList()
        );
    }

    // La nota no tiene título: primera línea no vacía del texto, recortada.
    private static string? TitleOf(string text) =>
        WorkLimits.ClipTitle(
            text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
        );
}
