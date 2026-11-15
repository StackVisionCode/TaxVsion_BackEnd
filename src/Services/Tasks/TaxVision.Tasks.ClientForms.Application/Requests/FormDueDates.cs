using BuildingBlocks.TimeZones;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Requests;

internal static class FormDueDates
{
    /// <summary>La fecha civil manda (P4): vence al final de ese día en la oficina. Sin ella, el instante tal cual.</summary>
    public static async Task<DateTime?> ResolveAsync(
        Guid tenantId,
        DateOnly? dueOn,
        DateTime? dueAtUtc,
        IOfficeTimeZone zones,
        CancellationToken ct
    ) => dueOn is { } civil ? LocalDay.EndUtc(civil, await zones.ResolveAsync(tenantId, ct)) : dueAtUtc;
}
