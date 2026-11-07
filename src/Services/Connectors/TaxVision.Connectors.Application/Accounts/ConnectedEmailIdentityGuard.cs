using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>
/// Regla de identidad de la sincronización: el buzón que se conecta DEBE ser el propio email del
/// usuario en el sistema (el email del JWT que emite Auth). Bloquea la "ocurrente idea" de
/// sincronizar el correo de otra persona o un buzón ajeno — tanto en SMTP/IMAP manual (donde el
/// email se teclea y no hay prueba de propiedad) como en OAuth (política estricta: aunque el
/// proveedor pruebe la propiedad del buzón, debe coincidir con el email de login).
///
/// <para>
/// El scoping por tenant es natural: el email del usuario ya es único por <c>(TenantId, Email)</c>
/// en Auth (índice <c>IX_Users_TenantId_Email</c>), así que la misma persona puede conectar su
/// correo en cada tenant al que pertenece, pero nunca el de un colega ni uno ajeno.
/// </para>
///
/// <para>Normaliza con <see cref="MailboxAddress"/>, igual que <c>TenantEmailAccount.Create</c> y <c>User</c>.</para>
/// </summary>
public static class ConnectedEmailIdentityGuard
{
    public static Result Ensure(string mailboxEmail, string? initiatorEmail)
    {
        var initiator = MailboxAddress.Create(initiatorEmail);
        if (initiator.IsFailure)
            return Result.Failure(
                new Error(
                    "Connectors.EmailIdentity.MissingInitiator",
                    "Could not determine your account email to verify mailbox ownership. Please sign in again."
                )
            );

        if (!initiator.Value.Matches(mailboxEmail))
            return Result.Failure(
                new Error(
                    "Connectors.EmailIdentity.Mismatch",
                    "You can only connect your own mailbox. The email address must match the account you signed in with."
                )
            );

        return Result.Success();
    }
}
