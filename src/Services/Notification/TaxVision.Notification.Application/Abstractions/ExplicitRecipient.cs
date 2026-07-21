namespace TaxVision.Notification.Application.Abstractions;

/// <summary>Destinatario ya conocido por el evento — comportamiento preexistente, sin cambios.</summary>
public sealed record ExplicitRecipient(string Email, Guid? UserId) : NotificationAudience;
