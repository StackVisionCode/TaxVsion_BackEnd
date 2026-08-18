namespace TaxVision.Notification.Application.Directory.Abstractions;

/// <param name="IsActive">Archivado o dado de baja: hay dirección, pero no corresponde escribirle.</param>
public sealed record RemoteCustomerContact(
    Guid TenantId,
    Guid CustomerId,
    string DisplayName,
    string PrimaryEmail,
    bool IsActive
);

public sealed record RemoteCustomerPage(IReadOnlyList<RemoteCustomerContact> Items, bool HasMore);

/// <summary>
/// Enumera la fuente autoritativa de clientes para reconciliar el directorio local.
///
/// <para>
/// Hace falta porque el directorio se llena por eventos, y los eventos sólo cubren lo que pasó
/// <b>desde</b> que el consumer existe: los clientes anteriores nunca entraron, y un evento perdido
/// deja un hueco permanente. Sin nadie que repase la fuente, el fallo es silencioso —el consumer del
/// correo no encuentra la dirección, sale sin error, y el cliente simplemente no recibe nada—.
/// </para>
/// </summary>
public interface INotificationCustomerClient
{
    /// <summary>Cross-tenant: pide el token de la PlatformTenant. Devuelve <c>null</c> ante cualquier fallo.</summary>
    Task<RemoteCustomerPage?> ListAllForReconciliationAsync(int page, int size, CancellationToken ct = default);
}
