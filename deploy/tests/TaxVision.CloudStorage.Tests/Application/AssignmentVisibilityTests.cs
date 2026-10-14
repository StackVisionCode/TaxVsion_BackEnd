using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// Filtro ABAC por asignación (empleado sin <c>customers.view_all</c>): se enciende cuando el resolver carga
/// el set en <see cref="StorageActorScope"/>. Flag off / view_all / portal mantienen el comportamiento previo.
/// </summary>
public sealed class AssignmentVisibilityTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static FileObject File(OwnerType ownerType, Guid? ownerId)
    {
        var key = ObjectKey.Create($"tenants/{Tenant:N}/{ownerType}/documents/2025/{Guid.NewGuid():N}.pdf").Value;
        return FileObject
            .Register(
                Guid.NewGuid(),
                Tenant,
                ownerType,
                ownerId,
                FolderType.Documents,
                2025,
                key,
                "doc.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(24)
            )
            .Value;
    }

    private static FakeFileObjectRepository SeedMixed(out Guid assignedCustomer, out Guid otherCustomer)
    {
        assignedCustomer = Guid.NewGuid();
        otherCustomer = Guid.NewGuid();
        var files = new FakeFileObjectRepository();
        files.Seed(File(OwnerType.Customer, assignedCustomer));
        files.Seed(File(OwnerType.Customer, otherCustomer));
        files.Seed(File(OwnerType.Tenant, null));
        return files;
    }

    [Fact]
    public async Task Staff_flag_off_sees_every_owner()
    {
        var files = SeedMixed(out _, out _);
        var scope = new StorageActorScope(false, null); // default Bypass=true

        var result = await ListFilesHandler.Handle(
            new ListFilesQuery(Tenant, scope, null, null, 0, 50),
            files,
            CancellationToken.None
        );

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task Staff_with_view_all_sees_every_owner_even_with_flag_on()
    {
        var files = SeedMixed(out _, out _);
        // view_all => resolver no toca Bypass (sigue true), aunque el flag esté encendido.
        var scope = new StorageActorScope(false, null);

        var result = await ListFilesHandler.Handle(
            new ListFilesQuery(Tenant, scope, null, null, 0, 50),
            files,
            CancellationToken.None
        );

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task Staff_without_assignment_hides_customer_owned_but_keeps_non_customer()
    {
        var files = SeedMixed(out _, out _);
        var scope = new StorageActorScope(false, null) { BypassAssignmentFilter = false, AssignedCustomerIds = [] };

        var result = await ListFilesHandler.Handle(
            new ListFilesQuery(Tenant, scope, null, null, 0, 50),
            files,
            CancellationToken.None
        );

        var only = Assert.Single(result);
        Assert.Equal(OwnerType.Tenant, only.OwnerType);
    }

    [Fact]
    public async Task Staff_with_assignment_sees_only_its_customers_and_non_customer()
    {
        var files = SeedMixed(out var assigned, out var other);
        var scope = new StorageActorScope(false, null)
        {
            BypassAssignmentFilter = false,
            AssignedCustomerIds = [assigned],
        };

        var result = await ListFilesHandler.Handle(
            new ListFilesQuery(Tenant, scope, null, null, 0, 50),
            files,
            CancellationToken.None
        );

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, f => f.OwnerId == other);
        Assert.Contains(result, f => f.OwnerId == assigned);
        Assert.Contains(result, f => f.OwnerType == OwnerType.Tenant);
    }

    [Fact]
    public async Task Portal_scope_is_untouched_by_assignment_filter()
    {
        var files = SeedMixed(out var assigned, out var other);
        // Portal: aun si llegara con el set no-null por error, su propio aislamiento gana (restrictedCustomerId).
        var scope = new StorageActorScope(true, assigned) { BypassAssignmentFilter = false, AssignedCustomerIds = [] };

        var result = await ListFilesHandler.Handle(
            new ListFilesQuery(Tenant, scope, OwnerType.Customer, other, 0, 50),
            files,
            CancellationToken.None
        );

        var only = Assert.Single(result);
        Assert.Equal(assigned, only.OwnerId);
    }

    [Fact]
    public void CanAccess_hides_unassigned_customer_file_for_staff_with_filter_on()
    {
        var assigned = Guid.NewGuid();
        var scope = new StorageActorScope(false, null)
        {
            BypassAssignmentFilter = false,
            AssignedCustomerIds = [assigned],
        };
        var mine = File(OwnerType.Customer, assigned);
        var other = File(OwnerType.Customer, Guid.NewGuid());
        var tenantOwned = File(OwnerType.Tenant, null);

        Assert.True(scope.CanAccess(mine));
        Assert.False(scope.CanAccess(other));
        Assert.True(scope.CanAccess(tenantOwned));
    }

    [Fact]
    public void CanAccess_default_scope_behaves_as_before_the_feature()
    {
        var scope = new StorageActorScope(false, null); // Bypass=true por defecto
        var customerFile = File(OwnerType.Customer, Guid.NewGuid());

        Assert.True(scope.CanAccess(customerFile));
    }
}
