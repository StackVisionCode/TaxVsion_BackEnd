using Quartz;

namespace TaxVision.Reminder.Infrastructure.Scheduling;

/// <summary>
/// Un solo lugar construye las claves de Quartz. El <b>group es el tenant</b> (ADR-R-05): con un
/// scheduler compartido, eso es lo que permite pausar o purgar todos los triggers de un tenant de
/// una sola llamada. Si la clave se armara ad-hoc en cada call site, esa capacidad se perdería en
/// cuanto alguien escribiera el string distinto.
/// </summary>
internal static class ReminderTriggerKeys
{
    internal const string TenantIdKey = "tenantId";
    internal const string ReminderIdKey = "reminderId";

    internal static TriggerKey For(Guid tenantId, Guid reminderId) =>
        new($"reminder:{reminderId:N}", GroupFor(tenantId));

    internal static string GroupFor(Guid tenantId) => $"tenant:{tenantId:N}";
}
