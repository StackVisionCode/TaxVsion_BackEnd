namespace TaxVision.Notification.Infrastructure.Reconciliation;

public sealed record RemoteTenantOwner(Guid TenantId, Guid UserId, DateTime RegisteredAtUtc, bool IsActive);
