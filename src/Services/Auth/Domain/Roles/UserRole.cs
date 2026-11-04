using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.Roles;

/// <summary>Unión N:M usuario–rol. Clave compuesta (UserId, RoleId).</summary>
public sealed class UserRole
{
    private UserRole() { }

    [UserReference(
        UserReferenceKind.Credential,
        HandledBy = "OffboardUserHandler",
        Note = "Se conserva; el estado terminal corta todo acceso."
    )]
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public DateTime AssignedAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid? AssignedByUserId { get; private set; }

    public static UserRole Create(Guid userId, Guid roleId, Guid? assignedByUserId = null) =>
        new()
        {
            UserId = userId,
            RoleId = roleId,
            AssignedAtUtc = DateTime.UtcNow,
            AssignedByUserId = assignedByUserId,
        };
}
