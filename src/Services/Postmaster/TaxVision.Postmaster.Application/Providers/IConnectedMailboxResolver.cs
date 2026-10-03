namespace TaxVision.Postmaster.Application.Providers;

/// <summary>
/// Resuelve el buzón activo de un tenant consultando la proyección local
/// <c>ConnectedMailbox</c> (D3 §4.3) — nunca llama a Connectors por red en esta resolución, mismo
/// criterio que <see cref="IProviderResolver"/> con <c>ITenantEmailProviderRepository</c>: el chequeo
/// local primero, la llamada de red cara (<see cref="IConnectedMailboxSender"/>) solo pasa si hay algo que enviar.
/// </summary>
public interface IConnectedMailboxResolver
{
    Task<MailboxResolveResult> ResolveAsync(Guid tenantId, CancellationToken ct);

    /// <summary>
    /// Resuelve una cuenta específica elegida explícitamente por el preparador (D3 Compose §10/§11.4)
    /// — a diferencia de <see cref="ResolveAsync"/>, que elige automáticamente "la más reciente" para
    /// notificaciones del sistema donde nadie está eligiendo. Falla limpio (nunca lanza) si la cuenta
    /// no pertenece al tenant o no está activa.
    /// </summary>
    Task<MailboxResolveResult> ResolveByAccountIdAsync(Guid tenantId, Guid accountId, CancellationToken ct);
}
