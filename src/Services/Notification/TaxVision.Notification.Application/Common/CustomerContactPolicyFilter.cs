using TaxVision.Notification.Application.Directory.Abstractions;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Canal transaccional que decide la política por canal del cliente (T7.2/T7.3). In-app no se
/// filtra aquí — es otro canal, el cliente del portal lo ve siempre que tenga cuenta activa.
/// </summary>
internal enum CustomerContactChannel
{
    Email,
    Sms,
}

/// <summary>
/// Puerta aditiva por canal (T7.3): consulta <see cref="CustomerContactPolicyProjection"/> y, si
/// no hay fila, devuelve true (opt-out — alineado con el default del VO en Customer). Debe
/// invocarse SOLO para notificaciones no críticas; los flows transaccionales (OTP, signer
/// invited) deben seguir enviándose siempre, aunque el cliente los tenga apagados.
/// </summary>
internal static class CustomerContactPolicyFilter
{
    public static async Task<bool> AllowsAsync(
        Guid tenantId,
        Guid customerId,
        CustomerContactChannel channel,
        ICustomerContactPolicyRepository repository,
        CancellationToken ct
    )
    {
        var policy = await repository.GetByCustomerIdAsync(tenantId, customerId, ct);
        if (policy is null)
            return true;
        return channel switch
        {
            CustomerContactChannel.Email => policy.EmailEnabled,
            CustomerContactChannel.Sms => policy.SmsEnabled,
            _ => true,
        };
    }

    /// <summary>
    /// Variante para flows de Signature (y similares) donde el destinatario puede ser cliente del
    /// tenant (<paramref name="customerId"/> != null) o un externo. Externos NO están sujetos a
    /// la política — ContactPolicy proyecta solo customers del tenant — y siempre reciben.
    /// </summary>
    public static Task<bool> AllowsForOptionalCustomerAsync(
        Guid tenantId,
        Guid? customerId,
        CustomerContactChannel channel,
        ICustomerContactPolicyRepository repository,
        CancellationToken ct
    ) => customerId is { } id ? AllowsAsync(tenantId, id, channel, repository, ct) : Task.FromResult(true);
}
