namespace TaxVision.Tasks.Application.Tasks.Abstractions;

/// <summary>Vencimiento de una lista filtrada desde el dashboard: los cortes de <c>GET /tasks/summary</c>.</summary>
public enum TaskDueBucket
{
    Overdue = 1,
    Today = 2,
    Soon = 3,

    /// <summary>Vencimientos legales del widget Filing deadlines: vencidos y los del horizonte de 30 días.</summary>
    Statutory = 4,
}

/// <param name="Window">Día civil de la oficina con el que se calcula el corte, el mismo del resumen.</param>
public sealed record TaskDueFilter(TaskDueBucket Bucket, TaskSummaryWindow Window);
