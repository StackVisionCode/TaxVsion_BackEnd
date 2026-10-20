namespace TaxVision.Notification.Application.Abstractions;

/// <summary>Un conjunto de usuarios ya identificados por el hecho (ej. asistentes, partes de una llamada).</summary>
public sealed record Participants(Guid TenantId, IReadOnlyList<Guid> UserIds) : NotificationAudience;
