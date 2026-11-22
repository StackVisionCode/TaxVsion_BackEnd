using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Application.Work;

namespace TaxVision.Tasks.Application.Tasks.Abstractions;

/// <summary>Resumen del dashboard agregado en SQL, con la visibilidad de lectura de tareas.</summary>
public interface ITaskSummaryReader
{
    Task<TaskSummaryResponse> ReadAsync(TaskSummaryFilter filter, CancellationToken ct);
}

/// <param name="VisibleTo">null = sin recorte (view_all o flag apagado).</param>
public sealed record TaskSummaryFilter(
    Guid TenantId,
    Guid? AssigneeUserId,
    Guid? CustomerId,
    int? TaxYear,
    Guid? VisibleTo,
    TaskSummaryWindow Window
);

/// <summary>
/// Cortes del día civil de la oficina: vencida = antes de hoy y "hoy" = hoy, como los widgets. "Próximas" sigue la
/// regla de <see cref="WorkWindow"/>.
/// </summary>
public sealed record TaskSummaryWindow(
    WorkWindow Work,
    DateTime TodayStartUtc,
    DateTime TomorrowStartUtc,
    DateTime DeadlinesEndUtc
)
{
    public DateOnly Today => Work.Today;

    /// <summary>Primer día fuera del horizonte de vencimientos legales (exclusivo).</summary>
    public DateOnly DeadlinesEnd => Today.AddDays(TaskSummaryLimits.DeadlineDays + 1);

    public DateTime DeadlinesEndMidnightUtc => DeadlinesEnd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public static TaskSummaryWindow For(DateTime nowUtc, ResolvedTimeZone officeZone)
    {
        var work = WorkWindow.For(nowUtc, officeZone);
        return new TaskSummaryWindow(
            work,
            LocalDay.StartUtc(work.Today, officeZone),
            LocalDay.StartUtc(work.Today.AddDays(1), officeZone),
            LocalDay.StartUtc(work.Today.AddDays(TaskSummaryLimits.DeadlineDays + 1), officeZone)
        );
    }
}

public static class TaskSummaryLimits
{
    public const int TopItems = 5;
    public const int DeadlineDays = 30;

    /// <summary>Filas legales que se agrupan en memoria; con más, la respuesta avisa con deadlinesTruncated.</summary>
    public const int DeadlineRows = 500;
}
