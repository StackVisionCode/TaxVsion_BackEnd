using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Tasks.Queries;

/// <summary>
/// Mismos filtros opcionales que el tablero. CanViewAll = el actor tiene customers.view_all; CanViewOffice = tiene
/// users.work.view (lo exige <see cref="DashboardScope.Office"/>).
/// </summary>
public sealed record GetTaskSummaryQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool CanViewAll,
    Guid? AssigneeUserId,
    Guid? CustomerId,
    int? TaxYear,
    DateTime NowUtc,
    DashboardScope Scope = DashboardScope.Default,
    bool CanViewOffice = false
);

public static class GetTaskSummaryHandler
{
    public static async Task<Result<TaskSummaryResponse>> Handle(
        GetTaskSummaryQuery query,
        ITaskSummaryReader reader,
        IOfficeTimeZone zones,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        if (query.Scope == DashboardScope.Office && !query.CanViewOffice)
            return Result.Failure<TaskSummaryResponse>(DashboardScopes.OfficeForbidden);

        // Misma regla que el tablero en todo alcance: sin view_all y con el flag, solo lo visible para el actor.
        var visibleTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var window = TaskSummaryWindow.For(query.NowUtc, await zones.ResolveAsync(query.TenantId, ct));
        var filter = new TaskSummaryFilter(
            query.TenantId,
            AssigneeOf(query),
            query.CustomerId,
            query.TaxYear,
            visibleTo,
            window
        );
        return Result.Success(await reader.ReadAsync(filter, ct));
    }

    // Me = asignadas al actor; Office = sin responsable; Default = el filtro pedido.
    private static Guid? AssigneeOf(GetTaskSummaryQuery query) =>
        query.Scope switch
        {
            DashboardScope.Me => query.ActorUserId,
            DashboardScope.Office => null,
            _ => query.AssigneeUserId,
        };
}
