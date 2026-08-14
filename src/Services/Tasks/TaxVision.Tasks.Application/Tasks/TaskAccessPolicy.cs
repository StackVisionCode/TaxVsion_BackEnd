using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks;

/// <summary>
/// Quién puede tocar una tarea ajena. Leerlas es de toda la firma; moverlas no.
/// </summary>
public static class TaskAccessPolicy
{
    /// <summary>
    /// Propia es la que le asignaron o la que creó. Para el resto hace falta el override de
    /// supervisión, que en una firma fiscal suele tener un preparador senior, no el admin.
    /// </summary>
    public static bool CanMutate(TaskItem task, Guid userId, bool hasManageAll) =>
        hasManageAll || task.AssigneeUserId == userId || task.CreatedByUserId == userId;
}
