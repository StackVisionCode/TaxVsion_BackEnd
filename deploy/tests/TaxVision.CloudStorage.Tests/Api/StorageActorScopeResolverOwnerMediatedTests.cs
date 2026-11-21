using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Tests.CustomerAccess;
using static TaxVision.CloudStorage.Tests.Api.StorageActorScopeResolverTests;

namespace TaxVision.CloudStorage.Tests.Api;

public sealed class StorageActorScopeResolverOwnerMediatedTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    [Theory]
    [InlineData("TenantEmployee")]
    [InlineData("TenantAdmin")]
    [InlineData("PlatformAdmin")]
    public async Task Staff_and_platform_admin_get_the_mark(string actorType)
    {
        var scope = await NewResolver(gate: true).ResolveAsync(User(actorType), Tenant, Actor, StaffScope(), default);

        Assert.True(scope.HidesOwnerMediated);
    }

    [Fact]
    public async Task The_portal_gets_the_mark()
    {
        var portal = new StorageActorScope(true, Guid.NewGuid()) { TenantId = Tenant };

        var scope = await NewResolver(gate: true).ResolveAsync(User("CustomerPortal"), Tenant, Actor, portal, default);

        Assert.True(scope.HidesOwnerMediated);
    }

    [Fact]
    public async Task A_service_never_gets_the_mark()
    {
        var service = new StorageActorScope(false, null) { TenantId = Tenant, IsService = true };

        var scope = await NewResolver(gate: true).ResolveAsync(User("Service"), Tenant, Actor, service, default);

        Assert.False(scope.HidesOwnerMediated);
    }

    [Fact]
    public async Task With_the_flag_off_nobody_gets_the_mark()
    {
        var resolver = NewResolver(gate: false);
        var portal = new StorageActorScope(true, Guid.NewGuid()) { TenantId = Tenant };

        var staff = await resolver.ResolveAsync(User("TenantEmployee"), Tenant, Actor, StaffScope(), default);
        var customer = await resolver.ResolveAsync(User("CustomerPortal"), Tenant, Actor, portal, default);

        Assert.False(staff.HidesOwnerMediated);
        Assert.False(customer.HidesOwnerMediated);
    }

    private static StorageActorScopeResolver NewResolver(bool gate) =>
        new(
            new FakeCustomerAssignmentProjectionStore(),
            new RecordingPermissions(),
            Options.Create(new CloudStorageVisibilityOptions { Enabled = false }),
            Options.Create(new CloudStoragePerItemAccessOptions { Enabled = false }),
            new FakeCustomerAccessRuleRepository(),
            Options.Create(new CloudStorageSourceReadGateOptions()),
            Options.Create(new CloudStorageOwnerMediatedGateOptions { Enabled = gate })
        );
}
