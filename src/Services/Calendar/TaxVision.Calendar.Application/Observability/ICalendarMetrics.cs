namespace TaxVision.Calendar.Application.Observability;

/// <summary>
/// Lo que hay que poder ver de este servicio sin entrar a la base.
///
/// <para>
/// <see cref="RecordExpansionDuration"/> es el termómetro: la consulta de rango carga <b>todas</b> las
/// series del tenant y las expande en memoria. Cuando esa medición empiece a subir es que un tenant se
/// acercó a las 2.000 series, y hay que cachear las expansiones antes de que se note en la UI.
/// </para>
/// </summary>
public interface ICalendarMetrics
{
    void RecordCreated(bool isRecurring);

    void RecordRescheduled(bool isRecurring);

    void RecordCancelled(bool isRecurring);

    void RecordExpansionDuration(double milliseconds, int seriesCount);

    void RecordConflictDetected(bool blocked);

    void RecordIcsFeedRequest(bool found);

    /// <summary>
    /// El feed se sirvió de la última copia buena porque la lectura en vivo falló. Sube en silencio
    /// para el usuario, así que si nadie la mira, una base caída pasa desapercibida durante un día.
    /// </summary>
    void RecordIcsFeedStale();
}
