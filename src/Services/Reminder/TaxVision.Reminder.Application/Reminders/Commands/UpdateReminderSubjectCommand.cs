using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Application.Reminders.Commands;

public sealed record UpdateReminderSubjectCommand(
    Guid TenantId,
    Guid UserId,
    Guid ReminderId,
    string? Title,
    string? Body
);

/// <summary>
/// Edición de texto: no toca la hora, así que <b>no</b> pasa por el scheduler. Resuelve el aggregate
/// por (tenant, usuario, id): un recordatorio ajeno se ve como inexistente, nunca como prohibido.
/// </summary>
public static class UpdateReminderSubjectHandler
{
    public static async Task<Result<ReminderResponse>> Handle(
        UpdateReminderSubjectCommand command,
        IReminderRepository reminders,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await reminders.GetOwnedAsync(command.TenantId, command.UserId, command.ReminderId, ct);
        if (found.IsFailure)
            return Result.Failure<ReminderResponse>(found.Error);

        var subject = ReminderSubject.Create(command.Title, command.Body);
        if (subject.IsFailure)
            return Result.Failure<ReminderResponse>(subject.Error);

        var reminder = found.Value;
        var changed = reminder.ChangeSubject(subject.Value);
        if (changed.IsFailure)
            return Result.Failure<ReminderResponse>(changed.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(ReminderResponse.From(reminder));
    }
}
