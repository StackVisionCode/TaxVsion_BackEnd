using System.Security.Claims;
using BuildingBlocks.Contracts.Files;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Tests.CustomerAccess;
using static TaxVision.CloudStorage.Tests.Application.Library.FakeLibraryReadRepository;

namespace TaxVision.CloudStorage.Tests.Application.Library;

/// <summary>
/// Orígenes con variantes (permiso + módulo + tipos de recurso). Se prueban con la definición de grabaciones, que
/// ya se ofrece.
/// </summary>
public sealed class LibrarySourceVariantsTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly LibrarySourceDefinition Recordings = LibrarySourceCatalog.Get(LibrarySource.Recordings);

    [Theory]
    [InlineData(new[] { "communication.call.start" }, new[] { "comms" }, new[] { "call" })]
    [InlineData(new[] { "communication.meeting.join" }, new[] { "meetings" }, new[] { "meeting" })]
    [InlineData(
        new[] { "communication.call.start", "communication.meeting.join" },
        new[] { "comms", "meetings" },
        new[] { "call", "meeting" }
    )]
    // Permiso sin su módulo: la variante no se cumple.
    [InlineData(new[] { "communication.call.start" }, new[] { "meetings" }, new string[0])]
    [InlineData(new string[0], new[] { "comms", "meetings" }, new string[0])]
    public async Task Each_variant_needs_its_own_permission_and_module(
        string[] permissions,
        string[] modules,
        string[] expected
    )
    {
        var resolver = Resolver(permissions, modules);

        var variants = await resolver.ReadableVariantsAsync(new ClaimsPrincipal(), Recordings, CancellationToken.None);

        Assert.Equal(expected, variants);
    }

    [Fact]
    public void A_source_with_one_variant_met_only_admits_that_variant_resource_types()
    {
        var access = Access(["call"]);

        var scope = LibraryAccessPolicy.ScopeFor(access, [LibrarySource.Recordings]);

        Assert.Equal(["call"], Assert.Single(scope.Sources).ResourceTypes);
        Assert.True(LibraryAccessPolicy.Admits(access, LibrarySource.Recordings, "call"));
        Assert.False(LibraryAccessPolicy.Admits(access, LibrarySource.Recordings, "meeting"));
    }

    [Fact]
    public void A_source_without_variants_admits_every_resource_type()
    {
        var access = LibraryAccessPolicyTests.Access();

        Assert.Null(
            Assert.Single(LibraryAccessPolicy.ScopeFor(access, [LibrarySource.Documents]).Sources).ResourceTypes
        );
        Assert.True(LibraryAccessPolicy.Admits(access, LibrarySource.Documents, null));
    }

    [Fact]
    public async Task Rows_of_the_unmet_variant_are_not_listed_even_if_the_owner_confirms_them()
    {
        var call = Recording("call", DateTime.UtcNow);
        var meeting = Recording("meeting", DateTime.UtcNow.AddMinutes(-1));
        var repository = new FakeLibraryReadRepository().Add(Tenant, call).Add(Tenant, meeting);
        var owners = new FakeFileOwnerVisibilityClient().Confirm(call.Id).Confirm(meeting.Id);

        var page = await LibraryPageAssembler.ReadAsync(
            new LibraryPageDependencies(
                repository,
                owners,
                new LibraryOwnerOptions(),
                new FakeSystemClock(DateTime.UtcNow)
            ),
            Tenant,
            Access(["call"]),
            [LibrarySource.Recordings],
            LibraryFilter.Create(new LibrarySearchRequest()).Value,
            CancellationToken.None
        );

        Assert.Equal([call.Id], page.Items.Select(item => item.Id));
        Assert.Equal("call_recording", page.Items[0].Kind);
    }

    [Fact]
    public async Task The_stage_two_sources_are_readable_and_accepted_by_the_filter()
    {
        var resolver = Resolver(
            ["communication.chat.reply", "communication.call.start", "communication.meeting.join", "notes.read"],
            ["comms", "meetings", "planner"]
        );

        var access = await resolver.ResolveAsync(new ClaimsPrincipal(), Tenant, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal([LibrarySource.Chat, LibrarySource.Recordings, LibrarySource.Notes], access.Readable);
        Assert.True(LibraryFilter.Create(new LibrarySearchRequest { Source = ["chat"] }).IsSuccess);
        Assert.True(LibraryFilter.Create(new LibrarySearchRequest { Source = ["recordings"] }).IsSuccess);
        Assert.True(LibraryFilter.Create(new LibrarySearchRequest { Source = ["notes"] }).IsSuccess);
    }

    private static LibraryAccess Access(string[] recordingVariants) =>
        LibraryAccessPolicyTests.Access(readable: [LibrarySource.Recordings], viewAll: true) with
        {
            Variants = new Dictionary<LibrarySource, IReadOnlyList<string>>
            {
                [LibrarySource.Recordings] = recordingVariants,
            },
        };

    private static LibraryRow Recording(string resourceType, DateTime at) =>
        Row(
            OwnerType.Communication,
            FolderType.Recordings,
            Guid.NewGuid(),
            resourceType,
            "recording",
            createdAtUtc: at
        );

    private static LibraryAccessResolver Resolver(string[] permissions, string[] modules) =>
        new(
            new Granted(permissions),
            new Modules(modules),
            new FakeCustomerAssignmentProjectionStore(),
            Options.Create(new CloudStorageVisibilityOptions())
        );

    private sealed class Granted(string[] permissions) : IUserPermissionsSource
    {
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default) =>
            Task.FromResult(permissions.Contains(permission));
    }

    private sealed class Modules(string[] modules) : ITenantModuleEntitlementsSource
    {
        public Task<bool> IsModuleEnabledAsync(ClaimsPrincipal user, string module, CancellationToken ct = default) =>
            Task.FromResult(modules.Contains(module));
    }
}
