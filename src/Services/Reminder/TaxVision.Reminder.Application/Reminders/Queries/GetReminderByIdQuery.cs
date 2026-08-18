using BuildingBlocks.Results;
using TaxVision.Reminder.Application.Reminders.Abstractions;

namespace TaxVision.Reminder.Application.Reminders.Queries;

public sealed record GetReminderByIdQuery(Guid TenantId, Guid UserId, Guid ReminderId);

/// <summary>
/// Lleva el <c>UserId</c> del token dentro del predicado SQL: un recordatorio ajeno se ve como
/// inexistente, nunca como prohibido.
/// </summary>
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
