namespace TaxVision.Auth.Domain.Roles;

/// <summary>Unión N:M usuario–rol. Clave compuesta (UserId, RoleId).</summary>
public sealed class UserRole
{
    private UserRole() { }

    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public DateTime AssignedAtUtc { get; private set; }
    public Guid? AssignedByUserId { get; private set; }

    public static UserRole Create(Guid userId, Guid roleId, Guid? assignedByUserId = null) =>
        new()
        {
            UserId = userId,
            RoleId = roleId,
            AssignedAtUtc = DateTime.UtcNow,
            AssignedByUserId = assignedByUserId
        };
}
