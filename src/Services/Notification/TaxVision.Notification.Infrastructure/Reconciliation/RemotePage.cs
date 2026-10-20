namespace TaxVision.Notification.Infrastructure.Reconciliation;

public sealed record RemotePage<T>(IReadOnlyList<T> Items, bool HasMore);
