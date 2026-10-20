namespace TaxVision.Notification.Infrastructure.Reconciliation;

public interface INotificationAuthReconciliationClient
{
    Task<RemotePage<RemoteTenantOwner>?> ListOwnersAsync(int page, int size, CancellationToken ct = default);
    Task<RemotePage<RemotePortalAccount>?> ListPortalAccountsAsync(int page, int size, CancellationToken ct = default);
}
