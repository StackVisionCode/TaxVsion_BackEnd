using BuildingBlocks.Contracts.Work;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.Appointments.Abstractions;

namespace TaxVision.Calendar.Application.Appointments.Work;

/// <summary>Títulos en vivo de citas para Activity. <c>CanViewAll</c> = customers.view_all del actor.</summary>
public sealed record GetAppointmentWorkTitlesQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool CanViewAll,
    IReadOnlyList<Guid> Ids
);

/// <summary>Solo lo que el actor puede leer en el detalle; lo demás se omite y lo vacío también.</summary>
public static class GetAppointmentWorkTitlesHandler
{
    public static async Task<IReadOnlyList<WorkTitle>> Handle(
        GetAppointmentWorkTitlesQuery query,
        IAppointmentTitleReader titles,
        IOptions<CalendarVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        if (query.Ids.Count == 0)
            return [];

        var assignedTo = GetAppointmentWorkHandler.AssignedTo(visibility.Value, query.ActorUserId, query.CanViewAll);
        var found = await titles.ListTitlesAsync(query.TenantId, query.Ids, assignedTo, ct);
        return
        [
            .. found
                .Select(t => (Title: t, Clipped: WorkLimits.ClipTitle(t.Title)))
                .Where(x => x.Clipped is not null)
                .Select(x => x.Title with { Title = x.Clipped! }),
        ];
    }
}
