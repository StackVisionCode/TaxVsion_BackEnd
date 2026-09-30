using TaxVision.Tasks.Domain.Series;

namespace TaxVision.Tasks.Application.Series;

/// <summary>
/// Quién puede pausar, reanudar o terminar una serie recurrente. Mismo criterio que
/// <see cref="TaxVision.Tasks.Application.Tasks.TaskAccessPolicy"/> con las tareas sueltas: la creó, o
/// tiene el override de supervisión.
///
/// <para>
/// Hasta la fase A1, los tres endpoints solo pedían <c>tasks.write</c>, así que cualquier empleado podía
/// terminar la serie de otro. Terminar no borra la instancia abierta, pero deja de sembrar las
/// siguientes: la tarea trimestral de un colega simplemente deja de aparecer, y no hay nada en la UI que
/// lo explique.
/// </para>
/// </summary>
public static class TaskSeriesAccessPolicy
{
    public static bool CanMutate(TaskSeries series, Guid userId, bool hasManageAll) =>
        hasManageAll || series.CreatedByUserId == userId;
}
