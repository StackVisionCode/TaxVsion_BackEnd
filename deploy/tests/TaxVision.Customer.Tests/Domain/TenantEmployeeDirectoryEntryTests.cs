using TaxVision.Customer.Domain.Employees;

namespace TaxVision.Customer.Tests.Domain;

/// <summary>
/// Bloquea la regla que la auditoria del track de chat tipado encontro sin proteger:
/// AssignPreparerHandler solo debe aceptar un PreparerUserId que sea un
/// TenantEmployee/TenantAdmin activo — nunca un CustomerPortal, un PlatformAdmin,
/// ni un empleado desactivado.
/// </summary>
public sealed class TenantEmployeeDirectoryEntryTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    [Theory]
    [InlineData("TenantEmployee")]
    [InlineData("TenantAdmin")]
    public void Active_TenantEmployee_or_TenantAdmin_is_an_eligible_preparer(string actorType)
    {
        var entry = TenantEmployeeDirectoryEntry.Create(UserId, TenantId, actorType, isActive: true);

        Assert.True(entry.IsEligiblePreparer);
    }

    [Theory]
    [InlineData("CustomerPortal")]
    [InlineData("PlatformAdmin")]
    public void CustomerPortal_or_PlatformAdmin_is_never_an_eligible_preparer(string actorType)
    {
        var entry = TenantEmployeeDirectoryEntry.Create(UserId, TenantId, actorType, isActive: true);

        Assert.False(entry.IsEligiblePreparer);
    }

    [Fact]
    public void Deactivated_TenantEmployee_is_not_an_eligible_preparer()
    {
        var entry = TenantEmployeeDirectoryEntry.Create(UserId, TenantId, "TenantEmployee", isActive: true);
        entry.MarkInactive();

        Assert.False(entry.IsEligiblePreparer);
    }

    [Fact]
    public void Reactivating_a_TenantEmployee_restores_eligibility()
    {
        var entry = TenantEmployeeDirectoryEntry.Create(UserId, TenantId, "TenantEmployee", isActive: true);
        entry.MarkInactive();
        entry.MarkActive();

        Assert.True(entry.IsEligiblePreparer);
    }

    [Fact]
    public void Offboarded_TenantEmployee_is_not_an_eligible_preparer()
    {
        var entry = TenantEmployeeDirectoryEntry.Create(UserId, TenantId, "TenantEmployee", isActive: true);
        entry.MarkOffboarded();

        Assert.True(entry.IsOffboarded);
        Assert.False(entry.IsActive);
        Assert.False(entry.IsEligiblePreparer);
    }

    [Fact]
    public void Offboard_is_terminal_a_reactivation_cannot_bring_the_employee_back()
    {
        var entry = TenantEmployeeDirectoryEntry.Create(UserId, TenantId, "TenantEmployee", isActive: true);
        entry.MarkOffboarded();
        entry.MarkActive();

        Assert.False(entry.IsEligiblePreparer);
    }

    [Fact]
    public void A_reconciled_offboarded_user_enters_already_offboarded()
    {
        var entry = TenantEmployeeDirectoryEntry.CreateReconciled(
            UserId,
            TenantId,
            "TenantEmployee",
            isActive: true,
            isOffboarded: true
        );

        Assert.True(entry.IsOffboarded);
        Assert.False(entry.IsActive);
        Assert.False(entry.IsEligiblePreparer);
    }

    [Fact]
    public void A_reconciled_admin_keeps_its_type_so_it_is_recognized_as_admin()
    {
        var entry = TenantEmployeeDirectoryEntry.CreateReconciled(
            UserId,
            TenantId,
            "TenantAdmin",
            isActive: true,
            isOffboarded: false
        );

        Assert.Equal("TenantAdmin", entry.ActorType);
        Assert.True(entry.IsEligiblePreparer);
    }

    [Fact]
    public void Reconciling_aligns_the_type_and_the_activity_with_Auth()
    {
        var entry = TenantEmployeeDirectoryEntry.Create(UserId, TenantId, "PlatformAdmin", isActive: false);

        Assert.True(entry.ReconcileWith("TenantAdmin", isActive: true));

        Assert.Equal("TenantAdmin", entry.ActorType);
        Assert.True(entry.IsEligiblePreparer);
    }

    [Fact]
    public void Reconciling_an_entry_that_already_matches_changes_nothing()
    {
        var entry = TenantEmployeeDirectoryEntry.Create(UserId, TenantId, "TenantEmployee", isActive: true);

        Assert.False(entry.ReconcileWith("TenantEmployee", isActive: true));
    }

    [Fact]
    public void Reconciling_never_revives_an_offboarded_employee()
    {
        var entry = TenantEmployeeDirectoryEntry.Create(UserId, TenantId, "TenantEmployee", isActive: true);
        entry.MarkOffboarded();

        Assert.False(entry.ReconcileWith("TenantEmployee", isActive: true));
        Assert.False(entry.IsEligiblePreparer);
    }
}
