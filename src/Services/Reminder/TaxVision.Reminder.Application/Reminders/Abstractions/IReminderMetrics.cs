using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Application.Reminders.Abstractions;

/// <summary>
/// Puerto de observabilidad del ciclo de vida de un recordatorio (`00_...` §8.3). Es un puerto y no
/// una clase estática porque los seis puntos de medición viven en <b>handlers de Application</b>
/// (crear, disparar, cancelar) y Application no puede depender de Infrastructure — la misma razón
/// por la que <c>CorrespondenceMetrics</c> solo se llama desde clientes de Infrastructure. El
/// precedente exacto es <c>IOnboardingMetrics</c> en Auth: puerto en Application, <c>Meter</c> en
/// Infrastructure.
///
/// <para>
/// Un método por hecho de negocio, con nombre propio (guardrail #2). Nada de
/// <c>Record(string metric, ...)</c>: eso volvería a esconder seis decisiones detrás de un string.
/// </para>
/// </summary>
public interface IReminderMetrics
{
    /// <summary>Un recordatorio nuevo quedó agendado. Tag <c>category</c>.</summary>
    void RecordScheduled(ReminderCategory category);

    /// <summary>El aviso salió de verdad. Tag <c>category</c>.</summary>
    void RecordFired(ReminderCategory category);

    /// <summary>
    /// Retraso real entre la hora agendada y el disparo. Es el termómetro directo del lag de
    /// consumers de `00_...` §8.1.
    /// </summary>
    void RecordFireDelaySeconds(double seconds);

    /// <summary>Terminó cancelado. Tag <c>reason</c> — pasarlo siempre por <c>ReminderCancellationReasons.ForMetrics</c>.</summary>
    void RecordCancelled(string reason);

    /// <summary>
    /// <b>La que importa</b>: se descartó un aviso. Si sube, hubo caída o el objetivo se movió al
    /// pasado. Tag <c>policy</c> — usar <see cref="ReminderMisfirePolicies"/>.
    /// </summary>
    void RecordMisfired(string policy);

    /// <summary>
    /// Cuántos redeliveries frenó la idempotencia de ADR-R-07. Si es 0 para siempre, la
    /// <c>RequestKey</c> está mal construida. Tag <c>resolution</c> — usar
    /// <see cref="ReminderDuplicateResolutions"/>.
    /// </summary>
    void RecordDuplicateSuppressed(string resolution);
}

/// <summary>Los dos caminos por los que un recordatorio llega a <c>Missed</c>.</summary>
public static class ReminderMisfirePolicies
{
    /// <summary>El disparo llegó más tarde que <c>Reminder:MisfireGraceMinutes</c>.</summary>
    public const string GraceExceeded = "grace_exceeded";

    /// <summary>El objetivo se movió hacia atrás y la hora recalculada ya había pasado.</summary>
    public const string AnchorMovedToPast = "anchor_moved_to_past";
}

/// <summary>Las dos capas de idempotencia de <c>CreateReminderHandler</c>.</summary>
public static class ReminderDuplicateResolutions
{
    /// <summary>Reintento normal: la consulta previa por <c>RequestKey</c> encontró el original.</summary>
    public const string Lookup = "lookup";

    /// <summary>Carrera real: dos peticiones simultáneas, la perdedora chocó contra el índice único.</summary>
    public const string UniqueIndexRace = "unique_index_race";
}
