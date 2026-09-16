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
    private UserPermissionDeny() { }

    public Guid UserId { get; private set; }
    public Guid PermissionId { get; private set; }
    public DateTime DeniedAtUtc { get; private set; }
    public Guid? DeniedByUserId { get; private set; }

    public static UserPermissionDeny Create(Guid userId, Guid permissionId, Guid? deniedByUserId = null) =>
        new()
        {
            UserId = userId,
            PermissionId = permissionId,
            DeniedAtUtc = DateTime.UtcNow,
            DeniedByUserId = deniedByUserId,
        };
}
