using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Timers.Abstractions;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

public sealed class TaskTimerRepository(TasksDbContext context) : ITaskTimerRepository
{
    /// <summary>
    /// Arranca de <c>Tasks</c> y baja a los timers: la tabla de timers no lleva tenant, se lo presta
    /// su tarea. Las horas se suman en SQL sobre los minutos transcurridos, no en memoria: traer
    /// tramo por tramo de una temporada entera no cabe.
    /// </summary>
    public async Task<IReadOnlyList<TaskTimerReportRow>> ListReportAsync(
        Guid tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        Guid? userId,
        CancellationToken ct = default
    )
    {
        var timers = context
            .Tasks.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId)
            .SelectMany(t => t.Timers, (task, timer) => new { task, timer })
            .Where(x =>
                x.timer.StoppedAtUtc != null && x.timer.StartedAtUtc >= fromUtc && x.timer.StartedAtUtc <= toUtc
            );

        if (userId is { } user)
            timers = timers.Where(x => x.timer.UserId == user);

        var grouped = await timers
            // El título entra como string, no como el VO: agrupar por el owned type haría que EF
            // proyecte una entidad propiedad sin su dueño y la consulta ni siquiera arranca.
            .GroupBy(x => new
            {
                x.task.Id,
                Title = x.task.Title.Value,
                x.timer.UserId,
                x.timer.IsBillable,
            })
            .Select(g => new
            {
                g.Key.Id,
                g.Key.Title,
                g.Key.UserId,
                g.Key.IsBillable,
                Minutes = g.Sum(x => EF.Functions.DateDiffMinute(x.timer.StartedAtUtc, x.timer.StoppedAtUtc)),
                Entries = g.Count(),
            })
            .ToListAsync(ct);

        return
        [
            .. grouped.Select(g => new TaskTimerReportRow(
                g.Id,
                g.Title,
                g.UserId,
                g.IsBillable,
                Math.Round((g.Minutes ?? 0) / 60m, 2),
                g.Entries
            )),
        ];
    }
}
