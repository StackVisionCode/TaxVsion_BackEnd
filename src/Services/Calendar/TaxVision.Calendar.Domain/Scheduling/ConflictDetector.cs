using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.Types;

namespace TaxVision.Calendar.Domain.Scheduling;

/// <summary>Qué se encontró al comprobar el hueco. <c>Blocks</c> decide si es 409 o sólo un aviso.</summary>
public sealed record ConflictCheckResult(bool HasConflict, bool Blocks, IReadOnlyList<Occurrence> Conflicting)
{
    public static readonly ConflictCheckResult None = new(false, false, []);
}

/// <summary>
/// Comprueba si una franja choca con lo que ya tiene la persona.
///
/// <para>
/// El solapamiento entre citas es <b>aviso por defecto</b>: un preparador puede querer solapar a
/// propósito, y bloquear siempre es paternalista. Bloquea sólo si el tipo lo pide —una firma
/// presencial no admite estar en dos sitios— y <b>siempre</b> si cae en un bloqueo de agenda: si está
/// de vacaciones, no es una advertencia.
/// </para>
/// </summary>
public static class ConflictDetector
{
    /// <summary>
    /// Al crear una serie se comprueban los próximos 90 días, no las 156 ocurrencias de tres años. El
    /// choque que importa es el de las próximas semanas; el resto se avisa cuando llegue.
    /// </summary>
    public const int SeriesLookaheadDays = 90;

    public static ConflictCheckResult Check(
        DateTime startUtc,
        DateTime endUtc,
        AppointmentType type,
        IReadOnlyList<Occurrence> existing,
        IReadOnlyList<BlockedTime> blocks
    )
    {
        foreach (var block in blocks)
        {
            if (block.Overlaps(startUtc, endUtc))
                return new ConflictCheckResult(true, Blocks: true, []);
        }

        var overlapping = new List<Occurrence>();
        foreach (var occurrence in existing)
        {
            if (startUtc < occurrence.EndUtc && endUtc > occurrence.StartUtc)
                overlapping.Add(occurrence);
        }

        return overlapping.Count == 0
            ? ConflictCheckResult.None
            : new ConflictCheckResult(true, type.BlocksOnConflict, overlapping);
    }

    /// <summary>
    /// Los huecos libres de una persona en un día: su horario de atención menos lo que ya tiene y
    /// menos sus bloqueos.
    ///
    /// <para>
    /// Devuelve <b>intervalos</b> y nada más. Quien pregunta por la disponibilidad de un compañero no
    /// tiene por qué enterarse de con quién se reúne.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TimeSlot> FreeSlots(
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        IReadOnlyList<TimeSlot> workingWindows,
        IReadOnlyList<Occurrence> busy,
        IReadOnlyList<BlockedTime> blocks,
        TimeSpan minimumSlot
    )
    {
        var taken = new List<TimeSlot>();
        foreach (var occurrence in busy)
            taken.Add(new TimeSlot(occurrence.StartUtc, occurrence.EndUtc));
        foreach (var block in blocks)
            taken.Add(new TimeSlot(block.StartUtc, block.EndUtc));

        taken.Sort((left, right) => left.StartUtc.CompareTo(right.StartUtc));

        var free = new List<TimeSlot>();
        foreach (var window in workingWindows)
        {
            var cursor = window.StartUtc < windowStartUtc ? windowStartUtc : window.StartUtc;
            var limit = window.EndUtc > windowEndUtc ? windowEndUtc : window.EndUtc;

            foreach (var slot in taken)
            {
                if (slot.EndUtc <= cursor || slot.StartUtc >= limit)
                    continue;

                if (slot.StartUtc - cursor >= minimumSlot)
                    free.Add(new TimeSlot(cursor, slot.StartUtc));

                if (slot.EndUtc > cursor)
                    cursor = slot.EndUtc;
            }

            if (limit - cursor >= minimumSlot)
                free.Add(new TimeSlot(cursor, limit));
        }

        return free;
    }
}

/// <summary>Un intervalo en UTC. Sin título, sin cliente, sin asistentes: sólo ocupado o libre.</summary>
public sealed record TimeSlot(DateTime StartUtc, DateTime EndUtc);
