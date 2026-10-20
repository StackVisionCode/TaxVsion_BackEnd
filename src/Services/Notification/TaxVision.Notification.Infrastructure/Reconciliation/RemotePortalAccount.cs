namespace TaxVision.Notification.Infrastructure.Reconciliation;

public sealed record RemotePortalAccount(Guid TenantId, Guid CustomerId, Guid UserId, bool IsActive);
