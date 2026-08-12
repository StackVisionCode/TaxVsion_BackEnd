using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Application.Consumers;

// ---------------------------------------------------------------------------
// Reminder Fase 10 — mantenimiento del directorio userId → email.
//
// Tres consumers, tres razones distintas:
//   · UserRegistered  → alta/actualización. Es el ÚNICO evento de Auth que trae el correo.
//   · SecurityAlert{email_changed} → INVALIDACIÓN. El cambio de correo confirmado no publica la
//     dirección nueva (el DetailsJson de ese evento trae la ANTERIOR), así que no se puede
//     actualizar: se marca la fila obsoleta y el siguiente envío la repuebla contra Auth. Una fila
//     obsoleta es peor que una ausente — la ausente dispara la recuperación pull, la obsoleta manda
//     el correo a la dirección vieja sin que nadie se entere.
//   · UserDeactivated / UserReactivated → hay dirección, pero cambia si corresponde escribirle.
//
// `UserProfileUpdated` NO aparece acá a propósito: solo lleva nombre y apellido.
// ---------------------------------------------------------------------------

public static class UserEmailDirectoryUpsertConsumer
{
    public static async Task Handle(
        UserRegisteredIntegrationEvent evt,
        IUserEmailDirectoryRepository directory,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var existing = await directory.FindAsync(evt.TenantId, evt.UserId, ct);
            if (existing is null)
                await directory.AddAsync(
                    UserEmailDirectoryEntry.Create(evt.TenantId, evt.UserId, evt.Email),
                    ct
                );
            else
                existing.UpdateEmail(evt.Email, isActive: true);

            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}

public static class UserEmailDirectoryInvalidationConsumer
{
    public static async Task Handle(
        SecurityAlertIntegrationEvent evt,
        IUserEmailDirectoryRepository directory,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<UserEmailDirectoryEntry> logger,
        CancellationToken ct
    )
    {
        if (evt.AlertType != SecurityAlertType.EmailChanged)
            return;

        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var existing = await directory.FindAsync(evt.TenantId, evt.UserId, ct);
            if (existing is null)
                return;

            existing.MarkStale();
            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation(
                "Email directory entry for user {UserId} in tenant {TenantId} marked stale after a confirmed "
                    + "email change; the next send will repopulate it from Auth.",
                evt.UserId,
                evt.TenantId
            );
        }
    }
}

public static class UserEmailDirectoryDeactivationConsumer
{
    public static async Task Handle(
        UserDeactivatedIntegrationEvent evt,
        IUserEmailDirectoryRepository directory,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) => await UserEmailDirectoryConsumerHelpers.SetActiveAsync(evt.TenantId, evt.UserId, false, directory, unitOfWork, correlation, ct);
}

public static class UserEmailDirectoryReactivationConsumer
{
    public static async Task Handle(
        UserReactivatedIntegrationEvent evt,
        IUserEmailDirectoryRepository directory,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) => await UserEmailDirectoryConsumerHelpers.SetActiveAsync(evt.TenantId, evt.UserId, true, directory, unitOfWork, correlation, ct);
}

internal static class UserEmailDirectoryConsumerHelpers
{
    internal static async Task SetActiveAsync(
        Guid tenantId,
        Guid userId,
        bool isActive,
        IUserEmailDirectoryRepository directory,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var existing = await directory.FindAsync(tenantId, userId, ct);
        if (existing is null)
            return;

        existing.SetActive(isActive);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
