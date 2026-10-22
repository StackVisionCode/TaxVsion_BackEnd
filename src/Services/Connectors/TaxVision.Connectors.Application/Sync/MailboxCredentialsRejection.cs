using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Domain.Accounts;
using Wolverine;

namespace TaxVision.Connectors.Application.Sync;

/// <summary>
/// El servidor IMAP rechazó las credenciales de la cuenta: pasa a Error y se avisa al usuario una sola vez.
/// Reintentar no sirve; hay que volver a conectar con la contraseña correcta.
/// </summary>
internal static class MailboxCredentialsRejection
{
    public const string ErrorCode = "Imap.AuthenticationFailed";

    public static async Task HandleAsync(
        TenantEmailAccount account,
        string reason,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger logger,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        // Si ya estaba en Error o desconectada, el usuario ya fue avisado.
        if (account.MarkCredentialsRejected(now).IsFailure)
            return;

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogWarning("IMAP credentials rejected for account {AccountId}; marked as Error.", account.Id);

        // Mismo evento que el refresh OAuth fallido: para el usuario es lo mismo, "reconecta tu buzón".
        await bus.PublishAsync(
            new ConnectorsOAuthRefreshFailedIntegrationEvent
            {
                TenantId = account.TenantId,
                CorrelationId = correlation.CorrelationId,
                AccountId = account.Id,
                ConnectionId = Guid.Empty,
                ProviderCode = account.ProviderCode.ToString(),
                Reason = reason,
                ErrorCode = ErrorCode,
                FailedAtUtc = now,
                CreatedByUserId = account.CreatedByUserId,
                IsOfficeMailbox = account.IsOffice,
                MailboxOwnerUserId = account.OwnerUserId,
            }
        );
    }
}
