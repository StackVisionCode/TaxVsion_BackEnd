using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.Scheduling;

namespace TaxVision.Calendar.Application.Office.TimeZoneChanges;

public sealed record GetOfficeTimeZoneChangePreviewQuery(Guid TenantId, DateTime NowUtc);

public sealed record OfficeTimeZoneChangePreview(
    Guid ChangeId,
    string FromTimeZoneId,
    string ToTimeZoneId,
    DateTime ChangedAtUtc,
    IReadOnlyList<MovableRule> Rules,
    IReadOnlyList<MovableSeries> Series
);

public sealed record MovableRule(Guid RuleId, Guid UserId, int DaysMask, string StartTime, string EndTime);

public sealed record MovableSeries(
    Guid AppointmentId,
    string Title,
    Guid OrganizerUserId,
    string LocalStartTime,
    DateTime NextStartUtc
);

/// <summary>
/// P9: la vista previa del cambio pendiente. Una serie entra solo si le queda alguna ocurrencia; una cita puntual nunca
/// (su instante ya está acordado). Sin cambio pendiente devuelve null.
/// </summary>
public static class GetOfficeTimeZoneChangePreviewHandler
{
    /// <summary>Ventana para saber si a una serie le queda alguna ocurrencia.</summary>
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(730);

    public static async Task<Result<OfficeTimeZoneChangePreview?>> Handle(
        GetOfficeTimeZoneChangePreviewQuery query,
        IOfficeTimeZoneChangeRepository changes,
        IOfficeTimeZoneCandidates candidates,
        CancellationToken ct
    )
    {
        var change = await changes.GetPendingAsync(query.TenantId, ct);
        if (change is null)
            return Result.Success<OfficeTimeZoneChangePreview?>(null);

        var rules = (await candidates.ActiveRulesInZoneAsync(query.TenantId, change.FromTimeZoneId, ct))
            .Select(rule => new MovableRule(
                rule.Id,
                rule.UserId,
                (int)rule.Days,
                rule.StartTime.ToString("HH:mm"),
                rule.EndTime.ToString("HH:mm")
            ))
            .ToList();

        var series = new List<MovableSeries>();
        foreach (var appointment in await candidates.LiveSeriesInZoneAsync(query.TenantId, change.FromTimeZoneId, ct))
        {
            var upcoming = OccurrenceExpander.Expand(appointment, query.NowUtc, query.NowUtc + Horizon);
            if (!HasUpcoming(appointment, query.NowUtc))
                continue;
            series.Add(
                new MovableSeries(
                    appointment.Id,
                    appointment.Title.Value,
                    appointment.OrganizerUserId,
                    appointment.Timing.LocalStartTime?.ToString("HH:mm") ?? string.Empty,
                    upcoming.Value.Min(o => o.StartUtc)
                )
            );
        }

        return Result.Success<OfficeTimeZoneChangePreview?>(
            new OfficeTimeZoneChangePreview(
                change.Id,
                change.FromTimeZoneId,
                change.ToTimeZoneId,
                change.ChangedAtUtc,
                rules,
                series
            )
        );
    }

    /// <summary>A la serie le queda alguna ocurrencia: es la que se puede mover.</summary>
    internal static bool HasUpcoming(Domain.Appointments.Appointment appointment, DateTime nowUtc)
    {
        var upcoming = OccurrenceExpander.Expand(appointment, nowUtc, nowUtc + Horizon);
        return upcoming.IsSuccess && upcoming.Value.Count > 0;
    }

    /// <summary>Hay algo que el admin podría mover en esa zona (horarios activos o series vivas).</summary>
    internal static async Task<bool> AnythingToMoveAsync(
        IOfficeTimeZoneCandidates candidates,
        Guid tenantId,
        string timeZoneId,
        DateTime nowUtc,
        CancellationToken ct
    ) =>
        (await candidates.ActiveRulesInZoneAsync(tenantId, timeZoneId, ct)).Count > 0
        || (await candidates.LiveSeriesInZoneAsync(tenantId, timeZoneId, ct)).Any(a => HasUpcoming(a, nowUtc));
}
