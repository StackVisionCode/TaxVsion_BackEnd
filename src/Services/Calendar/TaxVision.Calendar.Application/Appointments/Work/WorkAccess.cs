namespace TaxVision.Calendar.Application.Appointments.Work;

/// <summary>Permisos del que mira, leídos del JWT por el controller.</summary>
public sealed record WorkViewerRights(bool CanViewWork, bool CanManageAllCalendars, bool CanViewAllCustomers);

/// <summary>Quién puede ver el trabajo de agenda de quién (Work Profile).</summary>
public static class WorkAccess
{
    /// <summary>La propia agenda siempre; la de otro, solo con <see cref="CanSeeOthers"/>.</summary>
    public static bool CanSee(Guid actorUserId, Guid userId, WorkViewerRights rights) =>
        userId == actorUserId || CanSeeOthers(rights);

    /// <summary>users.work.view y además la regla de hoy para ver agendas ajenas (vista Team del CRM).</summary>
    public static bool CanSeeOthers(WorkViewerRights rights) =>
        rights.CanViewWork && (rights.CanManageAllCalendars || rights.CanViewAllCustomers);
}
