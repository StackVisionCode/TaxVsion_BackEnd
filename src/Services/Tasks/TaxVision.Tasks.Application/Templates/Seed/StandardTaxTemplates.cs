using TaxVision.Tasks.Application.Templates.Commands;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Templates.Seed;

/// <param name="DueOffsetDays">Negativo: días antes del vencimiento del encargo.</param>
public sealed record StandardTaxTemplate(
    string Name,
    string Description,
    string? RecurrenceRule,
    RecurrenceMode RecurrenceMode,
    IReadOnlyList<TaskTemplateStepDraft> Steps
);

/// <summary>
/// Los tres encargos que cubren el grueso del trabajo de una firma pequeña. Están en código y no en
/// la base de datos porque son el punto de partida que la firma edita, no un dato del tenant: cada
/// uno se copia una vez y a partir de ahí cada firma lo lleva a su manera.
/// </summary>
public static class StandardTaxTemplates
{
    /// <summary>Nueva York por defecto: los vencimientos del IRS se cuentan en hora del este.</summary>
    private const string DefaultTimeZoneId = "America/New_York";

    public static string TimeZoneId => DefaultTimeZoneId;

    public static IReadOnlyList<StandardTaxTemplate> All => [Individual1040, EstimatedTax1040Es, Payroll941];

    /// <summary>
    /// Seis pasos en cadena: nada empieza hasta que llegan los documentos, y nada se transmite hasta
    /// que el cliente aprobó. Los offsets salen del 15 de abril hacia atrás.
    /// </summary>
    private static StandardTaxTemplate Individual1040 =>
        new(
            "1040 — Declaración individual",
            "Del pedido de documentos al e-file, en el orden en que se hace.",
            null,
            RecurrenceMode.FixedSchedule,
            [
                Step(1, "Solicitar documentos al cliente", -60, TaskPriority.High, estimated: 0.5m),
                Step(2, "Revisar documentos recibidos", -35, TaskPriority.Normal, dependsOn: 1, estimated: 1m),
                Step(3, "Preparar la declaración", -21, TaskPriority.High, dependsOn: 2, estimated: 3m),
                Step(4, "Revisión interna", -14, TaskPriority.High, dependsOn: 3, estimated: 1m),
                Step(5, "Aprobación del cliente", -7, TaskPriority.Urgent, dependsOn: 4, estimated: 0.5m),
                Step(6, "Transmitir el e-file", 0, TaskPriority.Urgent, dependsOn: 5, statutory: true, estimated: 0.5m),
            ]
        );

    /// <summary>
    /// Los cuatro pagos estimados del año. Es una serie y no cuatro tareas sueltas: en enero el
    /// preparador no quiere ver en su lista el pago de septiembre.
    /// </summary>
    private static StandardTaxTemplate EstimatedTax1040Es =>
        new(
            "1040-ES — Pagos estimados trimestrales",
            "Los cuatro vencimientos del año: 15 de abril, junio, septiembre y enero siguiente.",
            "FREQ=YEARLY;BYMONTH=1,4,6,9;BYMONTHDAY=15",
            RecurrenceMode.FixedSchedule,
            [Step(1, "Calcular y enviar el pago estimado", 0, TaskPriority.High, statutory: true, estimated: 1m)]
        );

    private static StandardTaxTemplate Payroll941 =>
        new(
            "941 — Declaración trimestral de nómina",
            "El 941 de cada trimestre, con vencimiento el último día del mes siguiente al cierre.",
            "FREQ=YEARLY;BYMONTH=1,4,7,10;BYMONTHDAY=31",
            RecurrenceMode.FixedSchedule,
            [Step(1, "Preparar y transmitir el 941", 0, TaskPriority.High, statutory: true, estimated: 2m)]
        );

    private static TaskTemplateStepDraft Step(
        int order,
        string title,
        int dueOffsetDays,
        TaskPriority priority,
        int? dependsOn = null,
        bool statutory = false,
        decimal? estimated = null
    ) => new(order, title, null, priority, estimated, dueOffsetDays, statutory, dependsOn, null, null);
}
