using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Timers.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Timers.Queries;

/// <param name="UserId">Sin valor, el reporte cubre a toda la firma.</param>
/// <param name="CanViewOthers"><c>users.work.view</c>: sin él solo se ven las horas propias.</param>
public sealed record GetTaskTimerReportQuery(
    Guid TenantId,
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? UserId,
    Guid ActorUserId,
    bool CanViewOthers
);

public static class GetTaskTimerReportHandler
{
    public static async Task<Result<IReadOnlyList<TaskTimerReportRow>>> Handle(
        GetTaskTimerReportQuery query,
        ITaskTimerRepository timers,
        CancellationToken ct
    )
    {
        if (!query.CanViewOthers && query.UserId is { } other && other != query.ActorUserId)
            return Result.Failure<IReadOnlyList<TaskTimerReportRow>>(TaskErrors.Forbidden);

        // Sin permiso, "toda la oficina" son las horas propias.
        var userId = query.CanViewOthers ? query.UserId : query.ActorUserId;
        return Result.Success(await timers.ListReportAsync(query.TenantId, query.FromUtc, query.ToUtc, userId, ct));
    }
}
