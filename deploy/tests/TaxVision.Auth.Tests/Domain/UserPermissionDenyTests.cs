using TaxVision.Auth.Domain.Roles;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>
/// Phase 0 of the per-user permission overrides (RBAC deny layer). Covers the new join entity only —
/// the third RBAC join (UserPermissionDeny), mirroring UserRole. Still dormant: nothing reads it yet.
/// </summary>
public sealed class UserPermissionDenyTests
{
    [Fact]
    public void Create_sets_the_composite_key_and_audit_fields()
    {
        var userId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var deny = UserPermissionDeny.Create(userId, permissionId, actorId);

        Assert.Equal(userId, deny.UserId);
        Assert.Equal(permissionId, deny.PermissionId);
        Assert.Equal(actorId, deny.DeniedByUserId);
        Assert.NotEqual(default, deny.DeniedAtUtc);
    }

    [Fact]
    public void Create_allows_a_null_actor_for_system_or_backfill_paths()
    {
        var deny = UserPermissionDeny.Create(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(deny.DeniedByUserId);
    }
}
