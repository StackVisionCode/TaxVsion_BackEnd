namespace TaxVision.Calendar.Domain.Scheduling;

/// <summary>
/// Regla del negocio: una cita no se agenda ni se mueve al pasado. Cinco minutos de margen por el reloj
/// y por el tiempo de llenar el formulario (agendar "ahora" a las 10:02 para las 10:00 vale).
///
/// <para>Una serie ya empezada sí puede cambiar su hora: sus fechas pasadas no se reescriben.</para>
/// </summary>
public static class SchedulingWindow
{
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

    public static bool StartsInThePast(DateTime startUtc, DateTime nowUtc) => startUtc < nowUtc - Grace;
}
