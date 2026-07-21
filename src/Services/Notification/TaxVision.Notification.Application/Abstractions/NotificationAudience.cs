namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Fase 4 del plan de notificaciones dinámicas: quién debe recibir una notificación.
/// La mayoría de los ~16 consumers existentes de Notification siguen usando el patrón
/// anterior (destinatario explícito ya conocido por el evento — email/userId) y no se
/// tocan en esta fase. Este tipo cubre el caso nuevo: eventos que solo saben "avisar a
/// quien tenga tal permiso en el tenant" (hoy: los 2 consumers de CloudStorage que usaban
/// el placeholder de texto <c>"role:TenantAdmin"</c>).
/// </summary>
public abstract record NotificationAudience;
