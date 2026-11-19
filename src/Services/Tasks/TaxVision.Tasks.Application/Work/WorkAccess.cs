namespace TaxVision.Tasks.Application.Work;

/// <summary>Quién puede ver el trabajo de quién y con qué visibilidad se cuenta.</summary>
public static class WorkAccess
{
    /// <summary>El propio usuario siempre; otra persona solo con <c>users.work.view</c>.</summary>
    public static bool CanView(Guid actorUserId, Guid userId, bool canViewOthers) =>
        actorUserId == userId || canViewOthers;

    /// <summary>Mismo ABAC que el listado de tareas; null = sin restricción.</summary>
    public static Guid? VisibleTo(Guid actorUserId, bool canViewAll, bool assignmentVisibilityEnabled) =>
        assignmentVisibilityEnabled && !canViewAll ? actorUserId : null;
}
