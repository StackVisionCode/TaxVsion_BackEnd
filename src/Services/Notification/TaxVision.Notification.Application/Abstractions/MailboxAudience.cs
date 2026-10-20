namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Audiencia de un correo de buzón (D3): si <paramref name="OwnerUserId"/> viene, es un buzón personal y
/// solo lo recibe el dueño; si es null, es un buzón de oficina y lo reciben el preparador asignado al
/// cliente o, si no hay, quienes pueden ver el buzón de oficina. Siempre filtrado por acceso al cliente.
/// </summary>
public sealed record MailboxAudience(Guid TenantId, Guid AccountId, Guid? OwnerUserId, Guid CustomerId)
    : NotificationAudience;
