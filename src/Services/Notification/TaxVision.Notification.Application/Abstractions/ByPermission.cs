namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Todos los usuarios activos del tenant cuyo conjunto de permisos efectivos (unión de
/// todos sus roles) incluye <paramref name="PermissionCode"/>, resuelto contra la
/// proyección local <c>UserPermissionsProjection</c> — sin llamar a Auth por HTTP.
/// </summary>
public sealed record ByPermission(Guid TenantId, string PermissionCode) : NotificationAudience;
