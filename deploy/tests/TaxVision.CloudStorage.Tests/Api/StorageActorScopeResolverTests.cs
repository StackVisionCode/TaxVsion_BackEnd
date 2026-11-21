using System.Security.Claims;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Tests.CustomerAccess;

namespace TaxVision.CloudStorage.Tests.Api;

public sealed class StorageActorScopeResolverTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task A_service_token_keeps_its_scope_and_reads_no_permissions()
    {
        var permissions = new RecordingPermissions();
        var resolver = NewResolver(permissions, assignmentVisibility: true);
        var serviceScope = new StorageActorScope(false, null) { TenantId = Tenant, IsService = true };

        var scope = await resolver.ResolveAsync(User("Service"), Tenant, Actor, serviceScope, default);

        Assert.Same(serviceScope, scope);
        Assert.Empty(permissions.Asked);
    }

    [Fact]
    public async Task Staff_without_view_all_gets_the_assignment_filter()
    {
        var resolver = NewResolver(new RecordingPermissions(), assignmentVisibility: true);

        var scope = await resolver.ResolveAsync(User("TenantEmployee"), Tenant, Actor, StaffScope(), default);

        Assert.False(scope.BypassAssignmentFilter);
    }

    [Fact]
    public async Task With_the_gate_on_staff_gets_the_surfaces_it_can_read()
    {
        var resolver = NewResolver(new RecordingPermissions("invoicing.view", "signature.preparer.manage"), false);

        var scope = await resolver.ResolveAsync(User("TenantEmployee"), Tenant, Actor, StaffScope(), default);

        Assert.Equal([FileReadSurface.Billing, FileReadSurface.Signature], scope.ReadableSurfaces!.Order());
    }

    [Fact]
    public async Task With_the_gate_on_staff_without_permissions_gets_an_empty_set()
    {
        var resolver = NewResolver(new RecordingPermissions(), assignmentVisibility: true);

        var scope = await resolver.ResolveAsync(User("TenantEmployee"), Tenant, Actor, StaffScope(), default);

        Assert.NotNull(scope.ReadableSurfaces);
        Assert.Empty(scope.ReadableSurfaces);
    }

    [Fact]
    public async Task The_gate_also_applies_to_staff_with_view_all()
    {
        var resolver = NewResolver(new RecordingPermissions("customers.view_all"), assignmentVisibility: true);

        var scope = await resolver.ResolveAsync(User("TenantEmployee"), Tenant, Actor, StaffScope(), default);

        Assert.True(scope.BypassAssignmentFilter);
        Assert.Empty(scope.ReadableSurfaces!);
    }

    [Fact]
    public async Task With_the_gate_off_nothing_is_gated()
    {
        var resolver = NewResolver(new RecordingPermissions(), assignmentVisibility: false, sourceGate: false);

        var scope = await resolver.ResolveAsync(User("TenantEmployee"), Tenant, Actor, StaffScope(), default);

        Assert.Null(scope.ReadableSurfaces);
    }

    [Fact]
    public async Task The_portal_is_never_gated()
    {
        var resolver = NewResolver(new RecordingPermissions(), assignmentVisibility: true);
        var portal = new StorageActorScope(true, Guid.NewGuid()) { TenantId = Tenant };

        var scope = await resolver.ResolveAsync(User("CustomerPortal"), Tenant, Actor, portal, default);

        Assert.Null(scope.ReadableSurfaces);
    }

    internal static StorageActorScope StaffScope() => new(false, null) { TenantId = Tenant };

    internal static ClaimsPrincipal User(string actorType) =>
        new(new ClaimsIdentity([new Claim("actor_type", actorType)], "test"));

    private static StorageActorScopeResolver NewResolver(
        RecordingPermissions permissions,
        bool assignmentVisibility,
        bool sourceGate = true
    ) =>
        new(
            new FakeCustomerAssignmentProjectionStore(),
            permissions,
            Options.Create(new CloudStorageVisibilityOptions { Enabled = assignmentVisibility }),
            Options.Create(new CloudStoragePerItemAccessOptions { Enabled = false }),
            new FakeCustomerAccessRuleRepository(),
            Options.Create(new CloudStorageSourceReadGateOptions { Enabled = sourceGate }),
            Options.Create(new CloudStorageOwnerMediatedGateOptions())
        );

    /// <summary>Concede los permisos indicados y anota cada consulta.</summary>
    internal sealed class RecordingPermissions(params string[] granted) : IUserPermissionsSource
    {
        public List<string> Asked { get; } = [];

        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default)
        {
            Asked.Add(permission);
            return Task.FromResult(granted.Contains(permission));
        }
    }
}
