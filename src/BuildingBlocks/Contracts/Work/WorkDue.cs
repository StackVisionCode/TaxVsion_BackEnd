namespace BuildingBlocks.Contracts.Work;

/// <summary>
/// Regla común de vencido/próximo. Sin reloj: el que llama pasa la fecha de hoy de la oficina y, para
/// instantes, el ahora en UTC. Ventana "dueSoon" = hoy y los 6 días siguientes de la oficina.
/// </summary>
public static class WorkDue
{
    public const int DueSoonDays = 7;

    /// <summary>Primer día fuera de la ventana (exclusivo).</summary>
    public static DateOnly DueSoonEnd(DateOnly today) => today.AddDays(DueSoonDays);

    public static bool IsOverdue(DateOnly dueDate, DateOnly today) => dueDate < today;

    public static bool IsDueSoon(DateOnly dueDate, DateOnly today) => dueDate >= today && dueDate < DueSoonEnd(today);

    public static bool IsOverdue(DateTime dueAtUtc, DateTime nowUtc) => dueAtUtc < nowUtc;

    /// <summary>
    /// <paramref name="dueSoonEndUtc"/> = inicio en UTC del día <see cref="DueSoonEnd"/> en la zona de la oficina
    /// (p. ej. <c>LocalDay.StartUtc(WorkDue.DueSoonEnd(today), zone)</c>).
    /// </summary>
    public static bool IsDueSoon(DateTime dueAtUtc, DateTime nowUtc, DateTime dueSoonEndUtc) =>
        dueAtUtc >= nowUtc && dueAtUtc < dueSoonEndUtc;
}
