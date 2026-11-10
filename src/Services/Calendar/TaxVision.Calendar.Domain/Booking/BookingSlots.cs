using TaxVision.Calendar.Domain.Scheduling;

namespace TaxVision.Calendar.Domain.Booking;

/// <summary>
/// Parte los huecos libres en horarios reservables: comienzos alineados al paso de la política y con
/// la duración entera del tipo dentro del hueco.
///
/// <para>Ejemplo: hueco 9:00–10:10, 30 min, paso 15 → 9:00, 9:15, 9:30. 9:45 no entra (terminaría 10:15).</para>
/// </summary>
public static class BookingSlots
{
    public static IReadOnlyList<TimeSlot> Plan(IReadOnlyList<TimeSlot> free, TimeSpan duration, int stepMinutes)
    {
        var step = TimeSpan.FromMinutes(stepMinutes);
        var slots = new List<TimeSlot>();
        foreach (var window in free)
        {
            var start = AlignUp(window.StartUtc, step);
            while (start + duration <= window.EndUtc)
            {
                slots.Add(new TimeSlot(start, start + duration));
                start += step;
            }
        }

        return slots;
    }

    /// <summary>Al múltiplo del paso en minutos UTC: en zonas de hora entera cae en :00, :15, :30.</summary>
    private static DateTime AlignUp(DateTime value, TimeSpan step)
    {
        var remainder = value.Ticks % step.Ticks;
        return remainder == 0 ? value : new DateTime(value.Ticks - remainder + step.Ticks, DateTimeKind.Utc);
    }
}
