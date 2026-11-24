using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

/// <summary>
/// Resumen del dashboard en consultas fijas y cortas, sin traer filas para contarlas: estados agrupados, un COUNT por
/// métrica de vencimiento, completadas hoy, top, responsables de las vencidas y filas legales acotadas (se agrupan en
/// memoria). La regla civil se escribe una sola vez, en la fila.
/// </summary>
public sealed class TaskSummaryReader(TasksDbContext context) : ITaskSummaryReader
{
    private static readonly TaskItemStatus[] OpenStatuses = TaskItemStatuses.Open;

    // Filing deadlines: legales vencidas o dentro del horizonte. La usan el widget y la lista ?due=statutory.
    private static readonly Expression<Func<SummaryRow, bool>> FilingDeadline = r => r.IsStatutory && r.WithinDeadlines;

    public async Task<TaskSummaryResponse> ReadAsync(TaskSummaryFilter filter, CancellationToken ct)
    {
        var byStatus = await ByStatus(filter).ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        var overdue = await Overdue(filter).CountAsync(ct);
        var dueToday = await DueToday(filter).CountAsync(ct);
        var dueSoon = await DueSoon(filter).CountAsync(ct);
        var statutoryOverdue = await StatutoryOverdue(filter).CountAsync(ct);
        var completedToday = await Completed(filter).CountAsync(ct);
        var top = await Top(filter).ToListAsync(ct);
        var lateOwners = await OverdueAssignees(filter).ToListAsync(ct);
        var deadlines = await Deadlines(filter).ToListAsync(ct);
        return new TaskSummaryResponse(
            filter.Window.Today,
            byStatus.Values.Sum(),
            new TaskStatusCounts(
                byStatus.GetValueOrDefault(TaskItemStatus.NotStarted),
                byStatus.GetValueOrDefault(TaskItemStatus.InProgress),
                byStatus.GetValueOrDefault(TaskItemStatus.WaitingOnClient)
            ),
            overdue,
            dueToday,
            dueSoon,
            statutoryOverdue,
            completedToday,
            lateOwners.Count == 1 ? lateOwners[0] : null,
            [.. top.Select(ToItem)],
            GroupDeadlines(deadlines.Take(TaskSummaryLimits.DeadlineRows), filter.Window),
            deadlines.Count > TaskSummaryLimits.DeadlineRows
        );
    }

    // Las consultas internas las recorre la prueba de traducción a SQL Server. Un COUNT por métrica: el vencimiento
    // es un owned type y EF no lo traduce dentro de un agregado (GROUP BY con conteos condicionales).
    internal IQueryable<StatusCount> ByStatus(TaskSummaryFilter filter) =>
        Scoped(filter)
            .Where(t => OpenStatuses.Contains(t.Status))
            .GroupBy(t => t.Status)
            .Select(g => new StatusCount { Status = g.Key, Count = g.Count() });

    internal IQueryable<SummaryRow> Overdue(TaskSummaryFilter filter) =>
        OpenRows(filter, OverdueRange(filter.Window)).Where(r => r.Overdue);

    internal IQueryable<SummaryRow> DueToday(TaskSummaryFilter filter) =>
        OpenRows(filter, TodayRange(filter.Window)).Where(r => r.DueToday);

    internal IQueryable<SummaryRow> DueSoon(TaskSummaryFilter filter) =>
        OpenRows(filter, SoonRange(filter.Window)).Where(r => r.DueSoon);

    internal IQueryable<SummaryRow> StatutoryOverdue(TaskSummaryFilter filter) =>
        OpenRows(filter, OverdueRange(filter.Window)).Where(r => r.IsStatutory && r.Overdue);

    internal IQueryable<TaskItem> Completed(TaskSummaryFilter filter)
    {
        var (from, to) = (filter.Window.TodayStartUtc, filter.Window.TomorrowStartUtc);
        return Scoped(filter)
            .Where(t => t.Status == TaskItemStatus.Completed && t.CompletedAtUtc >= from && t.CompletedAtUtc < to);
    }

    // Vencidas primero (la más vieja arriba) y luego las de hoy.
    internal IQueryable<SummaryRow> Top(TaskSummaryFilter filter) =>
        OpenRows(filter, (DateTime.MinValue, TodayRange(filter.Window).Before))
            .Where(r => r.Overdue || r.DueToday)
            .OrderBy(r => r.DueAtUtc)
            .ThenBy(r => r.Id)
            .Take(TaskSummaryLimits.TopItems);

    // Dos bastan para saber si todas las vencidas son de una sola persona.
    internal IQueryable<Guid?> OverdueAssignees(TaskSummaryFilter filter) =>
        OpenRows(filter, OverdueRange(filter.Window))
            .Where(r => r.Overdue)
            .Select(r => r.AssigneeUserId)
            .Distinct()
            .Take(2);

    // Una fila de más para saber si se cortó.
    internal IQueryable<SummaryRow> Deadlines(TaskSummaryFilter filter) =>
        OpenRows(filter, DeadlinesRange(filter.Window))
            .Where(FilingDeadline)
            .OrderBy(r => r.DueAtUtc)
            .ThenBy(r => r.Id)
            .Take(TaskSummaryLimits.DeadlineRows + 1);

    // Tenant explícito e IgnoreQueryFilters (scope de Wolverine sin tenant ambiental), filtros del tablero y recorte.
    private IQueryable<TaskItem> Scoped(TaskSummaryFilter filter)
    {
        var query = context
            .Tasks.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .AsNoTracking()
            .Where(t => t.TenantId == filter.TenantId);
        if (filter.AssigneeUserId is { } assignee)
            query = query.Where(t => t.AssigneeUserId == assignee);
        if (filter.CustomerId is { } customer)
            query = query.Where(t => t.Reference.CustomerId == customer);
        if (filter.TaxYear is { } taxYear)
            query = query.Where(t => t.Reference.TaxYear == taxYear);
        return query.VisibleTo(context, filter.TenantId, filter.VisibleTo);
    }

    private IQueryable<SummaryRow> OpenRows(TaskSummaryFilter filter, (DateTime From, DateTime Before) due) =>
        Rows(Scoped(filter), filter.Window, due);

    /// <summary>Las abiertas de <paramref name="tasks"/> con ese vencimiento: el mismo corte que cuenta el resumen.</summary>
    internal static IQueryable<TaskItem> WithDue(IQueryable<TaskItem> tasks, TaskDueFilter due)
    {
        var window = due.Window;
        var ids = due.Bucket switch
        {
            TaskDueBucket.Overdue => Rows(tasks, window, OverdueRange(window)).Where(r => r.Overdue),
            TaskDueBucket.Today => Rows(tasks, window, TodayRange(window)).Where(r => r.DueToday),
            TaskDueBucket.Statutory => Rows(tasks, window, DeadlinesRange(window)).Where(FilingDeadline),
            _ => Rows(tasks, window, SoonRange(window)).Where(r => r.DueSoon),
        };
        var matching = ids.Select(r => r.Id);
        return tasks.Where(t => matching.Contains(t.Id));
    }

    // Rango buscable redundante de cada métrica: del menor al mayor de sus dos cortes. El OR civil no deja buscar por
    // (TenantId, Status, DueAtUtc) y el plan caía en el índice sin tenant; con el rango sí, y el resultado no cambia.
    private static (DateTime From, DateTime Before) OverdueRange(TaskSummaryWindow w) =>
        (DateTime.MinValue, Later(w.Work.TodayMidnightUtc, w.TodayStartUtc));

    private static (DateTime From, DateTime Before) TodayRange(TaskSummaryWindow w) =>
        (
            Earlier(w.Work.TodayMidnightUtc, w.TodayStartUtc),
            Later(w.Work.TodayMidnightUtc.AddDays(1), w.TomorrowStartUtc)
        );

    private static (DateTime From, DateTime Before) SoonRange(TaskSummaryWindow w) =>
        (Earlier(w.Work.TodayMidnightUtc, w.Work.NowUtc), Later(w.Work.DueSoonEndMidnightUtc, w.Work.DueSoonEndUtc));

    private static (DateTime From, DateTime Before) DeadlinesRange(TaskSummaryWindow w) =>
        (DateTime.MinValue, Later(w.DeadlinesEndMidnightUtc, w.DeadlinesEndUtc));

    private static DateTime Later(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Earlier(DateTime a, DateTime b) => a < b ? a : b;

    // Fecha civil de la oficina. Un vencimiento a medianoche UTC es una fecha civil del dato viejo (P5): se compara
    // por día. "Próximas" es la regla de TaskWorkReader.
    private static IQueryable<SummaryRow> Rows(
        IQueryable<TaskItem> tasks,
        TaskSummaryWindow window,
        (DateTime From, DateTime Before) due
    )
    {
        var work = window.Work;
        var (todayUtc, todayStart, tomorrowStart) = (
            work.TodayMidnightUtc,
            window.TodayStartUtc,
            window.TomorrowStartUtc
        );
        var (now, soonEnd, soonDay) = (work.NowUtc, work.DueSoonEndUtc, work.DueSoonEndMidnightUtc);
        var (deadlineDay, deadlineEnd) = (window.DeadlinesEndMidnightUtc, window.DeadlinesEndUtc);
        var (dueFrom, dueBefore) = due;
        // Disyuntiva redundante: antes del menor corte ya está vencida con cualquier hora. Sin ella SQL Server estima casi
        // cero filas para el OR con CONVERT(time) y, con pocas oficinas, elige el índice sin tenant con un lookup por fila.
        var (surelyOverdue, surelyWithin) = (Earlier(todayUtc, todayStart), Earlier(deadlineDay, deadlineEnd));
        return tasks
            .Where(t => OpenStatuses.Contains(t.Status) && t.Due!.DueAtUtc >= dueFrom && t.Due!.DueAtUtc < dueBefore)
            .Select(t => new SummaryRow
            {
                Id = t.Id,
                Title = t.Title.Value,
                Status = t.Status,
                AssigneeUserId = t.AssigneeUserId,
                CustomerId = t.Reference.CustomerId,
                DueAtUtc = (DateTime?)t.Due!.DueAtUtc,
                DueTimeZoneId = t.Due!.TimeZoneId,
                IsStatutory = (bool?)t.Due!.IsStatutory == true,
                Overdue =
                    (DateTime?)t.Due!.DueAtUtc != null
                    && (
                        t.Due.DueAtUtc < surelyOverdue
                        || (t.Due.DueAtUtc.TimeOfDay == TimeSpan.Zero && t.Due.DueAtUtc < todayUtc)
                        || (t.Due.DueAtUtc.TimeOfDay != TimeSpan.Zero && t.Due.DueAtUtc < todayStart)
                    ),
                DueToday =
                    (DateTime?)t.Due!.DueAtUtc != null
                    && (
                        (t.Due.DueAtUtc.TimeOfDay == TimeSpan.Zero && t.Due.DueAtUtc == todayUtc)
                        || (
                            t.Due.DueAtUtc.TimeOfDay != TimeSpan.Zero
                            && t.Due.DueAtUtc >= todayStart
                            && t.Due.DueAtUtc < tomorrowStart
                        )
                    ),
                DueSoon =
                    (DateTime?)t.Due!.DueAtUtc != null
                    && (
                        (
                            t.Due.DueAtUtc.TimeOfDay == TimeSpan.Zero
                            && t.Due.DueAtUtc >= todayUtc
                            && t.Due.DueAtUtc < soonDay
                        )
                        || (
                            t.Due.DueAtUtc.TimeOfDay != TimeSpan.Zero
                            && t.Due.DueAtUtc >= now
                            && t.Due.DueAtUtc < soonEnd
                        )
                    ),
                WithinDeadlines =
                    (DateTime?)t.Due!.DueAtUtc != null
                    && (
                        t.Due.DueAtUtc < surelyWithin
                        || (t.Due.DueAtUtc.TimeOfDay == TimeSpan.Zero && t.Due.DueAtUtc < deadlineDay)
                        || (t.Due.DueAtUtc.TimeOfDay != TimeSpan.Zero && t.Due.DueAtUtc < deadlineEnd)
                    ),
            });
    }

    // Misma fecha y mismo título (sin mayúsculas ni espacios de borde), con los clientes distintos; sin cliente
    // cuenta la tarea. La fecha es la de la tarea, como la que ve el usuario.
    private static IReadOnlyList<TaskDeadlineGroup> GroupDeadlines(
        IEnumerable<SummaryRow> rows,
        TaskSummaryWindow window
    ) =>
        rows.Select(row => (Row: row, DueOn: DueOn(row)))
            .Where(x => x.DueOn < window.DeadlinesEnd)
            .GroupBy(x => (x.DueOn, Title: x.Row.Title.Trim().ToLowerInvariant()))
            .Select(g => new TaskDeadlineGroup(
                g.Key.DueOn,
                g.First().Row.Title,
                g.Select(x => x.Row.CustomerId ?? x.Row.Id).Distinct().Count(),
                g.Count()
            ))
            .OrderBy(g => g.DueOn)
            .ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static TaskSummaryItem ToItem(SummaryRow row) =>
        new(
            row.Id,
            row.Title,
            row.Status,
            row.DueAtUtc,
            row.DueAtUtc is null ? null : DueOn(row),
            row.IsStatutory,
            row.AssigneeUserId,
            row.CustomerId
        );

    private static DateOnly DueOn(SummaryRow row)
    {
        var instant = DateTime.SpecifyKind(row.DueAtUtc!.Value, DateTimeKind.Utc);
        var due = DueDate.Create(instant, row.DueTimeZoneId, row.IsStatutory);
        return due.IsSuccess ? due.Value.DueOn : DateOnly.FromDateTime(instant);
    }

    internal sealed class SummaryRow
    {
        public Guid Id { get; init; }
        public string Title { get; init; } = string.Empty;
        public TaskItemStatus Status { get; init; }
        public Guid? AssigneeUserId { get; init; }
        public Guid? CustomerId { get; init; }
        public DateTime? DueAtUtc { get; init; }
        public string? DueTimeZoneId { get; init; }
        public bool IsStatutory { get; init; }
        public bool Overdue { get; init; }
        public bool DueToday { get; init; }
        public bool DueSoon { get; init; }
        public bool WithinDeadlines { get; init; }
    }

    internal sealed class StatusCount
    {
        public TaskItemStatus Status { get; init; }
        public int Count { get; init; }
    }
}
