using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Notifications.Preferences;

/// <summary>Autoservicio — TenantId/UserId vienen del JWT verificado del caller, nunca del body.</summary>
public sealed record SetNotificationPreferenceCommand(
    Guid TenantId,
    Guid UserId,
    NotificationCategory Category,
    NotificationChannel Channel,
    bool Enabled
);

public static class SetNotificationPreferenceHandler
{
    public static async Task<Result> Handle(
        SetNotificationPreferenceCommand command,
        IUserNotificationPreferenceRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var existing = await repository.GetAsync(
            command.TenantId,
            command.UserId,
            command.Category,
            command.Channel,
            ct
        );
        if (existing is not null)
        {
            existing.SetEnabled(command.Enabled);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }

        var created = UserNotificationPreference.Create(
            command.TenantId,
            command.UserId,
            command.Category,
            command.Channel,
            command.Enabled
        );
        if (created.IsFailure)
            return Result.Failure(created.Error);

        await repository.AddAsync(created.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
