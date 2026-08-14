namespace TaxVision.Tasks.Domain.Series;

/// <summary>
/// De dónde se siembra la expansión del RRULE. Es lo único que separa a los dos modos: el motor de
/// recurrencia es el mismo.
/// </summary>
public enum RecurrenceMode
{
    /// <summary>El calendario manda. Cerrar el 1040-ES de Q1 tarde no mueve el vencimiento de Q2.</summary>
    FixedSchedule = 1,

    /// <summary>Cuenta desde que se hizo. «Revisar la carpeta cada 90 días» corre desde el último repaso.</summary>
    AfterCompletion = 2,
}
