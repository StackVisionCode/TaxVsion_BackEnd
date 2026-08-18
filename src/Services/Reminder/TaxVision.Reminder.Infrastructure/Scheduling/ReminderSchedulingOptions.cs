namespace TaxVision.Reminder.Infrastructure.Scheduling;

/// <summary>
/// Sección <c>Reminder</c> de la configuración. La sección también trae <c>MaxSnoozeCount</c>, que
/// hoy vive duplicado como constante del aggregate — se unifica en la Fase 6, cuando exista un
/// handler que lo consuma; bindearlo acá sin consumidor solo agregaría una segunda fuente de verdad.
/// </summary>
public sealed class ReminderSchedulingOptions
{
    public const string SectionName = "Reminder";

    /// <summary>
    /// Ventana de gracia del misfire. Quartz <b>siempre</b> dispara al recuperarse de una caída; es
    /// el dominio quien decide si el aviso sigue vigente. Un retraso mayor que esto termina en
    /// <c>Missed</c> en vez de <c>Fired</c>: avisar «tenías reunión hace 3 horas» es ruido.
    /// </summary>
    public int MisfireGraceMinutes { get; set; } = 60;

    /// <summary>
    /// Cuánto hacia adelante mira la reconciliación. Reagendar recordatorios de dentro de un mes en
    /// cada barrido sería trabajo inútil: si el trigger falta, hay hasta 24 h para notarlo.
    /// </summary>
    public int ReconciliationHorizonHours { get; set; } = 24;

    /// <summary>
    /// Fase 10 — cuántos meses se conservan los recordatorios ya terminados
    /// (<c>Dismissed</c>/<c>Cancelled</c>/<c>Missed</c>). Un recordatorio terminal no vuelve a
    /// dispararse ni se muestra en ningún listado: lo único que hace al quedarse es engordar la
    /// tabla y el índice de `(TenantId, UserId, Status)`. En <b>0</b> el job no borra nada — es el
    /// interruptor para apagarlo sin tocar código.
    /// </summary>
    public int RetentionMonths { get; set; } = 12;

    public TimeSpan MisfireGrace => TimeSpan.FromMinutes(MisfireGraceMinutes);

    public TimeSpan ReconciliationHorizon => TimeSpan.FromHours(ReconciliationHorizonHours);
}
