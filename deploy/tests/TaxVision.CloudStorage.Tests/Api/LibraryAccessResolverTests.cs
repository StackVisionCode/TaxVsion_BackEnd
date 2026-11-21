using System.Security.Claims;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Tests.CustomerAccess;

namespace TaxVision.CloudStorage.Tests.Api;

public sealed class LibraryAccessResolverTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    private static readonly string[] Everything =
    [
        "cloudstorage.file.view",
        "signature.request.read",
        "invoicing.view",
        "financial.view",
        "cloudstorage.file.download",
    ];

    [Fact]
    public async Task With_every_permission_and_module_every_source_is_readable()
    {
        var access = await Resolve(Everything);

        Assert.Equal(
            [LibrarySource.Documents, LibrarySource.Signature, LibrarySource.Billing, LibrarySource.Financial],
            access.Readable
        );
        Assert.True(access.CanDownload);
    }

    [Theory]
    [InlineData("cloudstorage.file.view", LibrarySource.Documents)]
    [InlineData("signature.request.read", LibrarySource.Signature)]
    [InlineData("invoicing.view", LibrarySource.Billing)]
    [InlineData("financial.view", LibrarySource.Financial)]
    public async Task Each_missing_permission_removes_its_source(string missing, LibrarySource source)
    {
        var access = await Resolve(Everything.Where(p => p != missing).ToArray());

        Assert.DoesNotContain(source, access.Readable);
        Assert.Equal(3, access.Readable.Count);
    }

    [Theory]
    [InlineData("documents", LibrarySource.Documents)]
    [InlineData("signatures", LibrarySource.Signature)]
    [InlineData("financial_insights", LibrarySource.Financial)]
    public async Task A_disabled_module_removes_its_source_even_with_the_permission(string module, LibrarySource source)
    {
        var access = await Resolve(Everything, disabledModules: [module]);

        Assert.DoesNotContain(source, access.Readable);
    }

    [Fact]
    public async Task Billing_needs_no_module()
    {
        var access = await Resolve(
            Everything,
            disabledModules: ["documents", "signatures", "financial_insights", "billing", "invoicing"]
        );

        Assert.Equal([LibrarySource.Billing], access.Readable);
    }

    [Fact]
    public async Task Without_the_download_permission_nothing_can_be_downloaded()
    {
        var access = await Resolve(Everything.Where(p => p != "cloudstorage.file.download").ToArray());

        Assert.False(access.CanDownload);
    }

    [Fact]
    public async Task View_all_skips_the_assignments()
    {
        var customer = Guid.NewGuid();
        var assignments = new FakeCustomerAssignmentProjectionStore().Assign(Tenant, customer, Actor);

        var access = await Resolve([.. Everything, "customers.view_all"], assignments: assignments);

        Assert.True(access.ViewAll);
        Assert.Empty(access.AssignedCustomerIds);
    }

    [Fact]
    public async Task Without_view_all_the_assigned_clients_are_loaded_even_with_the_flag_off()
    {
        var customer = Guid.NewGuid();
        var assignments = new FakeCustomerAssignmentProjectionStore().Assign(Tenant, customer, Actor);

        var access = await Resolve(Everything, assignments: assignments, explorerFlag: false);

        Assert.False(access.ViewAll);
        Assert.False(access.ExplorerRestrict);
        Assert.Equal([customer], access.AssignedCustomerIds);
    }

    [Fact]
    public async Task The_explorer_flag_is_carried()
    {
        Assert.True((await Resolve(Everything, explorerFlag: true)).ExplorerRestrict);
    }

    [Fact]
    public async Task A_stale_token_propagates()
    {
        var resolver = new LibraryAccessResolver(
            new StalePermissions(),
            new Modules([]),
            new FakeCustomerAssignmentProjectionStore(),
            Options.Create(new CloudStorageVisibilityOptions())
        );

        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            resolver.ResolveAsync(User(), Tenant, Actor, default)
        );
        Assert.Equal("Auth.TokenStale", error.Message);
    }

    private static Task<LibraryAccess> Resolve(
        string[] permissions,
        string[]? disabledModules = null,
        FakeCustomerAssignmentProjectionStore? assignments = null,
        bool explorerFlag = false
    ) =>
        new LibraryAccessResolver(
            new StorageActorScopeResolverTests.RecordingPermissions(permissions),
            new Modules(disabledModules ?? []),
            assignments ?? new FakeCustomerAssignmentProjectionStore(),
            Options.Create(new CloudStorageVisibilityOptions { Enabled = explorerFlag })
        ).ResolveAsync(User(), Tenant, Actor, default);

    private static ClaimsPrincipal User() => StorageActorScopeResolverTests.User("TenantEmployee");

    private sealed class Modules(string[] disabled) : ITenantModuleEntitlementsSource
    {
        public Task<bool> IsModuleEnabledAsync(ClaimsPrincipal user, string module, CancellationToken ct = default) =>
            Task.FromResult(!disabled.Contains(module));
    }

    private sealed class StalePermissions : IUserPermissionsSource
    {
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default) =>
            throw new UnauthorizedAccessException("Auth.TokenStale");
    }
}
