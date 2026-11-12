using System.Globalization;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Application.Office.Abstractions;
using TaxVision.Calendar.Domain.Office;

namespace TaxVision.Calendar.Application.Office.Consumers;

/// <summary>
/// La oficina guardó su perfil: Calendar copia el horario y los días de cierre para recortar la
/// disponibilidad. Ordenado por versión e idempotente; un día u hora mal formados se descartan solos.
/// </summary>
public static class OfficeProfileScheduleConsumer
{
    public static async Task Handle(
        TenantProfileChangedIntegrationEvent evt,
        IOfficeScheduleRepository repository,
        IUnitOfWork unitOfWork,
        ILogger<OfficeScheduleProjection> logger,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        var projection = await repository.GetAsync(evt.ProfileTenantId, ct);
        if (projection is null)
        {
            projection = OfficeScheduleProjection.Create(evt.ProfileTenantId, now);
            repository.Add(projection);
        }

        if (!projection.ApplyProfile(evt.Version, Hours(evt), Closures(evt), now))
        {
            logger.LogInformation(
                "Office schedule v{Version} of tenant {TenantId} ignored (stale).",
                evt.Version,
                evt.ProfileTenantId
            );
            return;
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    private static List<OfficeHours> Hours(TenantProfileChangedIntegrationEvent evt)
    {
        var hours = new List<OfficeHours>();
        foreach (var day in evt.BusinessHours)
        {
            if (
                Enum.TryParse<DayOfWeek>(day.Day, ignoreCase: true, out var parsed)
                && TryTime(day.Opens, out var opens)
                && TryTime(day.Closes, out var closes)
                && closes > opens
            )
                hours.Add(new OfficeHours(parsed, opens, closes));
        }
        return hours;
    }

    private static List<OfficeClosureSpan> Closures(TenantProfileChangedIntegrationEvent evt)
    {
        var closures = new List<OfficeClosureSpan>();
        foreach (var closure in evt.Closures)
        {
            if (!TryDate(closure.Date, out var date))
                continue;
            DateOnly? end = TryDate(closure.EndDate, out var parsed) && parsed >= date ? parsed : null;
            closures.Add(new OfficeClosureSpan(date, end, closure.Label, closure.RepeatsYearly));
        }
        return closures;
    }

    private static bool TryTime(string? raw, out TimeOnly time) =>
        TimeOnly.TryParseExact(raw, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static bool TryDate(string? raw, out DateOnly date) =>
        DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
