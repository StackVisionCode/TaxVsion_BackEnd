using TaxVision.Connectors.Application.Watch;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>Lo que la unidad deja tocar, atado a su DbContext.</summary>
public sealed record MailboxFailureStores(
    ITenantEmailAccountRepository Accounts,
    IProviderWatchSubscriptionRepository Subscriptions
);

/// <summary>
/// Registra la falla de un buzón (estado + aviso) en una sola confirmación, después de hablar con el proveedor y
/// fuera de esa llamada. La falla puede aparecer dentro de un handler o en un job, que no tiene transacción: un
/// <c>SaveChanges</c> suelto perdería el evento de dominio y dejaría la cuenta en Error sin aviso (CON-08).
/// </summary>
public interface IMailboxFailureUnit
{
    /// <summary>
    /// <paramref name="work"/> devuelve <c>true</c> para confirmar. Si la unidad propia falla no relanza: no queda
    /// nada y la próxima pasada vuelve a detectar la falla.
    /// </summary>
    Task RunAsync(Func<MailboxFailureStores, CancellationToken, Task<bool>> work, CancellationToken ct = default);
}
