namespace TaxVision.Subscription.Application.Subscriptions.Commands.Resume;

/// <summary>Deshace una cancelación programada antes de que llegue el fin del período.</summary>
public sealed record ResumeSubscriptionCommand(Guid TenantId, Guid RequestedByUserId);
