using BuildingBlocks.Common;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Application.Sync;

/// <summary>
/// El servidor IMAP rechazó las credenciales de la cuenta: pasa a Error y el agregado levanta el aviso, una sola vez.
/// Reintentar no sirve; hay que volver a conectar con la contraseña correcta.
/// </summary>
internal static class MailboxCredentialsRejection
{
    public const string ErrorCode = "Imap.AuthenticationFailed";

    /// <returns>True si la cuenta pasó a Error: hay que guardar, y el guardado lleva el aviso.</returns>
    public static bool TryMark(
        TenantEmailAccount account,
        string reason,
        ICorrelationContext correlation,
        ILogger logger
    )
    {
        var cause = new TenantEmailAccountErrorCause(
            TenantEmailAccountErrorSource.ImapCredentials,
            ErrorCode,
            reason,
            correlation.CorrelationId
        );
        // Si ya estaba en Error o desconectada, el usuario ya fue avisado.
        if (account.MarkCredentialsRejected(cause, DateTime.UtcNow).IsFailure)
            return false;

        logger.LogWarning("IMAP credentials rejected for account {AccountId}; marked as Error.", account.Id);
        return true;
    }
}
