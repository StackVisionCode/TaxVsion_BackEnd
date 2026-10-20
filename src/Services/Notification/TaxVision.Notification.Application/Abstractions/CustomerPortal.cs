namespace TaxVision.Notification.Application.Abstractions;

/// <summary>El usuario de la cuenta de portal activa del cliente (resuelto contra una proyección local).</summary>
public sealed record CustomerPortal(Guid TenantId, Guid CustomerId) : NotificationAudience;
