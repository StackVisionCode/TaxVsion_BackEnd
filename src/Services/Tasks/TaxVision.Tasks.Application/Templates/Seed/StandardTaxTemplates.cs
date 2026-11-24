using TaxVision.Tasks.Application.Templates.Commands;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Templates;

namespace TaxVision.Tasks.Application.Templates.Seed;

/// <param name="Key">Clave estable: identifica la estándar aunque cambie su nombre.</param>
/// <param name="PreviousNames">Nombres con que se instaló antes; sirven para reconocer copias sin clave.</param>
public sealed record StandardTaxTemplate(
    string Key,
    IReadOnlyList<string> PreviousNames,
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
    public static IReadOnlyList<StandardTaxTemplate> All => [Individual1040, EstimatedTax1040Es, Payroll941];

    public static StandardTaxTemplate? ByKey(string? key) => All.FirstOrDefault(s => s.Key == key);

    /// <summary>La estándar de la que viene una plantilla: por su clave o, si no la tiene, por un nombre conocido.</summary>
    public static StandardTaxTemplate? Of(TaskTemplate template) =>
        template.StandardKey is { } key
            ? ByKey(key)
            : All.FirstOrDefault(s =>
                s.Name.Equals(template.Name, StringComparison.OrdinalIgnoreCase)
                || s.PreviousNames.Contains(template.Name, StringComparer.OrdinalIgnoreCase)
            );

    /// <summary>¿Tiene la regla y los pasos de la versión vigente? El nombre y la descripción no cuentan.</summary>
    public static bool IsCurrent(TaskTemplate template, StandardTaxTemplate standard)
    {
        if (
            template.Recurrence?.Value != standard.RecurrenceRule
            || (standard.RecurrenceRule is not null && template.RecurrenceMode != standard.RecurrenceMode)
        )
            return false;
        var steps = template.Steps.OrderBy(s => s.Order).ToList();
        return steps.Count == standard.Steps.Count
            && steps
                .Zip(standard.Steps)
                .All(pair =>
                    pair.First.Order == pair.Second.Order
                    && pair.First.Title.Value == pair.Second.Title
                    && pair.First.DueOffsetDays == pair.Second.DueOffsetDays
                    && pair.First.Priority == pair.Second.Priority
                    && pair.First.IsStatutory == pair.Second.IsStatutory
                    && pair.First.DependsOnStepOrder == pair.Second.DependsOnStepOrder
                );
    }

    /// <summary>
    /// Seis pasos en cadena: nada empieza hasta que llegan los documentos, y nada se transmite hasta
    /// que el cliente aprobó. Los offsets salen del 15 de abril hacia atrás.
    /// </summary>
    private static StandardTaxTemplate Individual1040 =>
        new(
            "1040",
            ["1040 — Declaración individual"],
            "1040 — Individual Return",
            "From the document request to e-file, in the order the work happens.",
            null,
            RecurrenceMode.FixedSchedule,
            [
                Step(1, "Request documents from the client", -60, TaskPriority.High, estimated: 0.5m),
                Step(2, "Review received documents", -35, TaskPriority.Normal, dependsOn: 1, estimated: 1m),
                Step(3, "Prepare the return", -21, TaskPriority.High, dependsOn: 2, estimated: 3m),
                Step(4, "Internal review", -14, TaskPriority.High, dependsOn: 3, estimated: 1m),
                Step(5, "Client approval", -7, TaskPriority.Urgent, dependsOn: 4, estimated: 0.5m),
                Step(6, "Transmit the e-file", 0, TaskPriority.Urgent, dependsOn: 5, statutory: true, estimated: 0.5m),
            ]
        );

    /// <summary>
    /// Los cuatro pagos estimados del año. Es una serie y no cuatro tareas sueltas: en enero el
    /// preparador no quiere ver en su lista el pago de septiembre.
    /// </summary>
    private static StandardTaxTemplate EstimatedTax1040Es =>
        new(
            "1040-es",
            ["1040-ES — Pagos estimados trimestrales"],
            "1040-ES — Quarterly Estimated Payments",
            "The four due dates of the year: April 15, June, September, and the following January.",
            "FREQ=YEARLY;BYMONTH=1,4,6,9;BYMONTHDAY=15",
            RecurrenceMode.FixedSchedule,
            [
                Step(
                    1,
                    "Calculate and submit the estimated payment",
                    0,
                    TaskPriority.High,
                    statutory: true,
                    estimated: 1m
                ),
            ]
        );

    private static StandardTaxTemplate Payroll941 =>
        new(
            "941",
            ["941 — Declaración trimestral de nómina"],
            "941 — Quarterly Payroll Return",
            "The 941 for each quarter, due the last day of the month after the quarter closes.",
            "FREQ=YEARLY;BYMONTH=1,4,7,10;BYMONTHDAY=-1",
            RecurrenceMode.FixedSchedule,
            [Step(1, "Prepare and transmit the 941", 0, TaskPriority.High, statutory: true, estimated: 2m)]
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
