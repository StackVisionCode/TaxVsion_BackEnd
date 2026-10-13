namespace TaxVision.Campaigns.Domain.Scheduling;

/// <summary>
/// Frecuencia de una campaña recurrente. Los presets con nombre calculan el próximo disparo por calendario
/// (<see cref="Monthly"/> = mismo día del mes siguiente) o por intervalo fijo; <see cref="Custom"/> usa
/// <c>IntervalMinutes</c> para casos finos (cada N minutos/horas).
/// </summary>
public enum RecurrenceFrequency
{
    Hourly = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
    Custom = 4,
}
