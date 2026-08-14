using BuildingBlocks.Common;
using TaxVision.Reminder.Application.Reminders.Abstractions;

namespace TaxVision.Reminder.Application.Reminders.Queries;

public sealed record ListUpcomingRemindersQuery(
    Guid TenantId,
    Guid UserId,
    DateTime FromUtc,
    DateTime ToUtc,
    int Page,
    int Size
);

/// <summary>
/// La agenda: solo lo que todavía va a sonar. Un rango invertido devuelve vacío por construcción —
/// no hace falta un error de dominio para algo que la UI no puede producir. El <c>UserId</c> viaja
/// dentro del predicado SQL, no como filtro posterior (ver <see cref="ListMyRemindersHandler"/>).
/// </summary>
public static class ListUpcomingRemindersHandler
{
    public static async Task<PagedResult<ReminderResponse>> Handle(
        ListUpcomingRemindersQuery query,
        IReminderRepository reminders,
        CancellationToken ct
    )
    {
        var result = await reminders.ListUpcomingForUserAsync(
            query.TenantId,
            query.UserId,
            query.FromUtc,
            query.ToUtc,
            query.Page,
            query.Size,
            ct
        );
        return ReminderResponse.FromPage(result);
    }
}
