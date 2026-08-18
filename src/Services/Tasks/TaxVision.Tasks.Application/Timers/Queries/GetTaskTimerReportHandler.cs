using TaxVision.Tasks.Application.Timers.Abstractions;

namespace TaxVision.Tasks.Application.Timers.Queries;

/// <param name="UserId">Sin valor, el reporte cubre a toda la firma.</param>
public sealed record GetTaskTimerReportQuery(Guid TenantId, DateTime FromUtc, DateTime ToUtc, Guid? UserId);

public static class GetTaskTimerReportHandler
{
    public static async Task<IReadOnlyList<TaskTimerReportRow>> Handle(
        GetTaskTimerReportQuery query,
        ITaskTimerRepository timers,
        CancellationToken ct
    ) => await timers.ListReportAsync(query.TenantId, query.FromUtc, query.ToUtc, query.UserId, ct);
}
