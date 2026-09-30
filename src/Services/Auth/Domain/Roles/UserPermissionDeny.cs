namespace TaxVision.Auth.Domain.Roles;

/// <summary>
/// N:M user–permission denial. Composite key (UserId, PermissionId). A row means "this user is
/// explicitly denied this permission, regardless of what their roles grant": the effective permission
/// set is (union of the user's role permissions) minus these denies, and a deny always wins. This is the
/// third RBAC join, completing <see cref="RolePermission"/> (what a role grants) and <see cref="UserRole"/>
/// (which roles a user has). It lives in the Auth bounded context and never leaves it as a deny — Auth
/// subtracts denies before publishing the final effective permission codes. Mirrors <see cref="UserRole"/>.
/// </summary>
public sealed class UserPermissionDeny
{
    /// <summary>Tope de la razón, alineado con la columna. Se recorta, no se rechaza.</summary>
    public const int ReasonMaxLength = 256;

    private UserPermissionDeny() { }

    public Guid UserId { get; private set; }
    public Guid PermissionId { get; private set; }
    public DateTime DeniedAtUtc { get; private set; }
    public Guid? DeniedByUserId { get; private set; }

    /// <summary>Por qué se denegó, para el registro de auditoría y para que la UI lo muestre. Opcional.</summary>
    public string? Reason { get; private set; }

    /// <summary>
    /// Momento en que el deny deja de aplicar. <c>null</c> = indefinido. Un deny vencido sigue en la
    /// tabla (queda el rastro) pero ya no resta: lo filtran la consulta de permisos efectivos y el
    /// job que los limpia.
    /// </summary>
    public DateTime? ExpiresAtUtc { get; private set; }

    public static UserPermissionDeny Create(
        Guid userId,
        Guid permissionId,
        Guid? deniedByUserId = null,
        string? reason = null,
        DateTime? expiresAtUtc = null
    ) =>
        new()
        {
            UserId = userId,
            PermissionId = permissionId,
            DeniedAtUtc = DateTime.UtcNow,
            DeniedByUserId = deniedByUserId,
            Reason = Trim(reason),
            ExpiresAtUtc = expiresAtUtc,
        };

    /// <summary>true si el deny ya no aplica en el momento dado.</summary>
    public bool IsExpired(DateTime nowUtc) => ExpiresAtUtc is { } expiry && expiry <= nowUtc;

    private static string? Trim(string? reason)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        return trimmed.Length <= ReasonMaxLength ? trimmed : trimmed[..ReasonMaxLength];
    }
}
