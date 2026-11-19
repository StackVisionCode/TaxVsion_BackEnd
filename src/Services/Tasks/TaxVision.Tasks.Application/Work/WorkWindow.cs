using BuildingBlocks.Contracts.Work;
using BuildingBlocks.TimeZones;

namespace TaxVision.Tasks.Application.Work;

/// <summary>
/// Hoy de la oficina y los cortes de vencido/próximo según <see cref="WorkDue"/>. Las fechas civiles se comparan con
/// <see cref="Today"/>; los instantes con <see cref="NowUtc"/> y <see cref="DueSoonEndUtc"/>.
/// </summary>
public sealed record WorkWindow(DateOnly Today, DateTime NowUtc, DateTime DueSoonEndUtc)
{
    public DateOnly DueSoonEnd => WorkDue.DueSoonEnd(Today);

    /// <summary>Medianoche UTC de hoy: así guarda un vencimiento civil el dato viejo (P5).</summary>
    public DateTime TodayMidnightUtc => Today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public DateTime DueSoonEndMidnightUtc => DueSoonEnd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public static WorkWindow For(DateTime nowUtc, ResolvedTimeZone officeZone)
    {
        var today = CivilDate.Of(nowUtc, officeZone);
        return new WorkWindow(today, nowUtc, LocalDay.StartUtc(WorkDue.DueSoonEnd(today), officeZone));
    }
}
