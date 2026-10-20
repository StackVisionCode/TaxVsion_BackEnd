namespace TaxVision.Notification.Application.Abstractions;

/// <summary>El owner del tenant (resuelto contra una proyección local). Recibe lo obligatorio de billing (D2).</summary>
public sealed record TenantOwnerAudience(Guid TenantId) : NotificationAudience;
