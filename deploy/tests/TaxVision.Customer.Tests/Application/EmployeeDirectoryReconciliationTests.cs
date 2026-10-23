using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Employees.Reconciliation;
using TaxVision.Customer.Domain.Employees;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// Una página del staff de Auth aplicada al directorio de empleados: entra quien nunca llegó por evento
/// (los owners anteriores a la proyección) y se corrige lo desfasado, sin retirar a nadie ni tocar filas
/// de otro tenant.
/// </summary>
public sealed class EmployeeDirectoryReconciliationTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private sealed class InMemoryDirectory : ITenantEmployeeDirectoryReconciliationRepository
    {
        public Dictionary<Guid, TenantEmployeeDirectoryEntry> Entries { get; } = [];

        public Task<IReadOnlyDictionary<Guid, TenantEmployeeDirectoryEntry>> GetByUserIdsAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyDictionary<Guid, TenantEmployeeDirectoryEntry>>(
                Entries.Where(pair => userIds.Contains(pair.Key)).ToDictionary()
            );

        public void Add(TenantEmployeeDirectoryEntry entry) => Entries.Add(entry.UserId, entry);
    }

    private static Task<EmployeeDirectoryReconciliationResult> ApplyAsync(
        InMemoryDirectory directory,
        params RemoteStaffUser[] users
    ) => EmployeeDirectoryReconciliation.ApplyPageAsync(users, directory, NullLogger.Instance, CancellationToken.None);

    [Fact]
    public async Task A_missing_owner_is_created_as_an_active_TenantAdmin()
    {
        var directory = new InMemoryDirectory();
        var owner = new RemoteStaffUser(Tenant, Guid.NewGuid(), "TenantAdmin", IsActive: true, IsOffboarded: false);

        var result = await ApplyAsync(directory, owner);

        Assert.Equal(new EmployeeDirectoryReconciliationResult(1, 0), result);
        var entry = directory.Entries[owner.UserId];
        Assert.Equal(("TenantAdmin", Tenant), (entry.ActorType, entry.TenantId));
        Assert.True(entry.IsEligiblePreparer);
    }

    [Fact]
    public async Task Existing_entries_are_corrected_and_matching_ones_are_left_alone()
    {
        var directory = new InMemoryDirectory();
        var stale = TenantEmployeeDirectoryEntry.Create(Guid.NewGuid(), Tenant, "TenantEmployee", isActive: true);
        var current = TenantEmployeeDirectoryEntry.Create(Guid.NewGuid(), Tenant, "TenantEmployee", isActive: true);
        directory.Add(stale);
        directory.Add(current);

        var result = await ApplyAsync(
            directory,
            new RemoteStaffUser(Tenant, stale.UserId, "TenantEmployee", IsActive: false, IsOffboarded: false),
            new RemoteStaffUser(Tenant, current.UserId, "TenantEmployee", IsActive: true, IsOffboarded: false)
        );

        Assert.Equal(new EmployeeDirectoryReconciliationResult(0, 1), result);
        Assert.False(stale.IsActive);
        Assert.True(current.IsActive);
    }

    [Fact]
    public async Task A_user_offboarded_in_Auth_is_only_deactivated_because_offboarding_belongs_to_its_event()
    {
        var directory = new InMemoryDirectory();
        var leaving = TenantEmployeeDirectoryEntry.Create(Guid.NewGuid(), Tenant, "TenantEmployee", isActive: true);
        directory.Add(leaving);

        await ApplyAsync(
            directory,
            new RemoteStaffUser(Tenant, leaving.UserId, "TenantEmployee", IsActive: false, IsOffboarded: true)
        );

        Assert.False(leaving.IsActive);
        Assert.False(leaving.IsOffboarded);
    }

    [Fact]
    public async Task An_entry_of_another_tenant_is_never_rewritten()
    {
        var directory = new InMemoryDirectory();
        var foreign = TenantEmployeeDirectoryEntry.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "TenantEmployee",
            isActive: true
        );
        directory.Add(foreign);

        var result = await ApplyAsync(
            directory,
            new RemoteStaffUser(Tenant, foreign.UserId, "TenantAdmin", IsActive: false, IsOffboarded: false)
        );

        Assert.Equal(new EmployeeDirectoryReconciliationResult(0, 0), result);
        Assert.Equal("TenantEmployee", foreign.ActorType);
        Assert.True(foreign.IsActive);
    }
}
