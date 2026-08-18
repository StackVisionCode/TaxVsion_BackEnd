using BuildingBlocks.Common;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Domain.Reminders;

namespace TaxVision.Reminder.Application.Reminders.Queries;

public sealed record ListMyRemindersQuery(Guid TenantId, Guid UserId, ReminderStatus? Status, int Page, int Size);

/// <summary>
/// El <c>UserId</c> viaja dentro del predicado SQL, no como filtro posterior: filtrar en memoria
/// después de paginar rompería <c>TotalCount</c> y dejaría páginas cortas — y, peor, el conteo
/// revelaría cuántos recordatorios ajenos hay.
/// </summary>
public static class ListMyRemindersHandler
{
    public static async Task<PagedResult<ReminderResponse>> Handle(
        ListMyRemindersQuery query,
        IReminderRepository reminders,
        CancellationToken ct
    )
    {
        var result = await reminders.ListForUserAsync(
            query.TenantId,
            query.UserId,
            query.Status,
            query.Page,
            query.Size,
            ct
        );
        return ReminderResponse.FromPage(result);
    }
}
