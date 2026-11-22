using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// <c>UserProfileUpdated</c> es una foto del perfil (nombre, idioma, zona): la guarda en el directorio para
/// escribirle a cada usuario en su idioma sin llamar a Auth. Sin fila no la crea (el correo es obligatorio en
/// la fila): la lectura remota la traerá completa. Una foto más vieja que la guardada se ignora.
/// </summary>
public static class UserProfileDirectoryConsumer
{
    public static async Task Handle(
        UserProfileUpdatedIntegrationEvent evt,
        IUserEmailDirectoryRepository directory,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<UserEmailDirectoryEntry> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var entry = await directory.FindAsync(evt.TenantId, evt.UserId, ct);
            if (entry is null)
            {
                logger.LogInformation(
                    "Profile of user {UserId} in tenant {TenantId} skipped: not in the local directory yet.",
                    evt.UserId,
                    evt.TenantId
                );
                return;
            }
            if (
                !entry.ApplyProfile(evt.Language, evt.TimeZoneId, DisplayNameOf(evt.Name, evt.LastName), evt.OccurredOn)
            )
                return;
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    internal static string? DisplayNameOf(string? name, string? lastName)
    {
        var full = $"{name?.Trim()} {lastName?.Trim()}".Trim();
        return full.Length == 0 ? null : full;
    }
}
