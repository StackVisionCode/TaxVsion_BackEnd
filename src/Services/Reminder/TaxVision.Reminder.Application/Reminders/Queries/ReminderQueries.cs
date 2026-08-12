using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Domain.Reminders;

namespace TaxVision.Reminder.Application.Reminders.Queries;

// ---------------------------------------------------------------------------
// Las tres lecturas del servicio. Todas llevan el UserId del token dentro del predicado SQL, no
// como filtro posterior: filtrar en memoria después de paginar rompería TotalCount y dejaría
// páginas cortas — y, peor, el conteo revelaría cuántos recordatorios ajenos hay.
// ---------------------------------------------------------------------------

public sealed record GetReminderByIdQuery(Guid TenantId, Guid UserId, Guid ReminderId);

public static class GetReminderByIdHandler
{
    public static async Task<Result<ReminderResponse>> Handle(
        GetReminderByIdQuery query,
        IReminderRepository reminders,
        CancellationToken ct
    )
    {
        var found = await reminders.GetOwnedAsync(query.TenantId, query.UserId, query.ReminderId, ct);
        return found.IsFailure
            ? Result.Failure<ReminderResponse>(found.Error)
            : Result.Success(ReminderResponse.From(found.Value));
    }
}

public sealed record ListMyRemindersQuery(Guid TenantId, Guid UserId, ReminderStatus? Status, int Page, int Size);

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
        return ToResponse(result);
    }

    internal static PagedResult<ReminderResponse> ToResponse(PagedResult<ReminderAggregate> result) =>
        new(result.Items.Select(ReminderResponse.From).ToList(), result.Page, result.Size, result.TotalCount);
}

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
/// no hace falta un error de dominio para algo que la UI no puede producir.
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
        return ListMyRemindersHandler.ToResponse(result);
    }
}
