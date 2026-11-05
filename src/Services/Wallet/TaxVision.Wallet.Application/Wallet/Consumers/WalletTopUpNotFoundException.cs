namespace TaxVision.Wallet.Application.Wallet.Consumers;

/// <summary>
/// Pago de recarga confirmado sin orden en Wallet. Es permanente (reintentar no la hace aparecer):
/// va directo a la cola de errores para revisión humana.
/// </summary>
public sealed class WalletTopUpNotFoundException(Guid tenantId, Guid topUpId, Guid saaSPaymentId)
    : Exception($"WalletTopUp {topUpId} of tenant {tenantId} not found for succeeded payment {saaSPaymentId}.")
{
    public Guid TenantId { get; } = tenantId;
    public Guid TopUpId { get; } = topUpId;
    public Guid SaaSPaymentId { get; } = saaSPaymentId;
}
