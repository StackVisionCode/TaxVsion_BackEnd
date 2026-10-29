namespace TaxVision.Tasks.ClientForms.Application.Insights;

/// <summary>
/// Ventana del período: N días locales completos que terminan hoy (hora local de la oficina vía el
/// desfase que manda el navegador) hasta ahora. El período anterior tiene la misma duración y termina
/// donde empieza este, para que el delta compare tramos iguales.
/// </summary>
public sealed record FormInsightsWindow(
    int Days,
    DateTime FromUtc,
    DateTime ToUtc,
    DateTime PreviousFromUtc,
    DateOnly FirstLocalDay,
    int UtcOffsetMinutes,
    FormInsightsGranularity Granularity
)
{
    public const int DefaultDays = 30;
    public const int MaxOffsetMinutes = 14 * 60;
    public static readonly IReadOnlyList<int> AllowedDays = [7, 30, 90];

    public static bool IsAllowed(int days) => AllowedDays.Contains(days);

    public int BucketDays => Granularity == FormInsightsGranularity.Week ? 7 : 1;

    public int BucketCount => (Days + BucketDays - 1) / BucketDays;

    public static FormInsightsWindow Create(int days, int utcOffsetMinutes, DateTime nowUtc)
    {
        var normalizedDays = IsAllowed(days) ? days : DefaultDays;
        var offset = Math.Clamp(utcOffsetMinutes, -MaxOffsetMinutes, MaxOffsetMinutes);
        var localToday = DateOnly.FromDateTime(nowUtc.AddMinutes(offset));
        var firstLocalDay = localToday.AddDays(-(normalizedDays - 1));
        var fromUtc = DateTime.SpecifyKind(
            firstLocalDay.ToDateTime(TimeOnly.MinValue).AddMinutes(-offset),
            DateTimeKind.Utc
        );
        return new(
            normalizedDays,
            fromUtc,
            nowUtc,
            fromUtc - (nowUtc - fromUtc),
            firstLocalDay,
            offset,
            normalizedDays > 30 ? FormInsightsGranularity.Week : FormInsightsGranularity.Day
        );
    }
}
