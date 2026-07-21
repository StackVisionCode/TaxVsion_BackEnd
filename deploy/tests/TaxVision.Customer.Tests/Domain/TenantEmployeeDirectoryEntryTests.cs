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
}
