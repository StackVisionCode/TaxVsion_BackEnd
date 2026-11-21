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
/// El origen <c>notes</c> se ofrece con <c>notes.read</c> y el módulo <c>planner</c>; cada fila la confirma Notes y su
/// marca de privacidad llega tal cual.
/// </summary>
public sealed class LibrarySourceNotesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Notes_is_offered_last_and_accepted_by_the_filter()
    {
        Assert.Equal("notes", LibrarySourceCatalog.Published[^1].Id);
        Assert.Equal(
            [LibrarySource.Notes],
            LibraryFilter.Create(new LibrarySearchRequest { Source = ["notes"] }).Value.Sources
        );
    }

    [Theory]
    [InlineData(new[] { "notes.read" }, new[] { "planner" }, true)]
    [InlineData(new[] { "notes.read" }, new string[0], false)]
    [InlineData(new string[0], new[] { "planner" }, false)]
    public async Task Notes_needs_its_permission_and_the_planner_module(
        string[] permissions,
        string[] modules,
        bool readable
    )
    {
        var resolver = new LibraryAccessResolver(
            new Granted(permissions),
            new Modules(modules),
            new FakeCustomerAssignmentProjectionStore(),
            Options.Create(new CloudStorageVisibilityOptions())
        );

        var access = await resolver.ResolveAsync(new ClaimsPrincipal(), Tenant, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(readable, access.Readable.Contains(LibrarySource.Notes));
    }

    [Fact]
    public async Task A_note_attachment_confirmed_by_notes_is_listed_with_its_restriction()
    {
        var noteId = Guid.NewGuid();
        var row = Row(OwnerType.Customer, FolderType.Other, Guid.NewGuid(), "note", "attachment");
        var repository = new FakeLibraryReadRepository().Add(Tenant, row);
        var owners = new FakeFileOwnerVisibilityClient().Confirm(
            new VisibleFile(row.Id, "note", noteId, "Call Ana", null, RestrictedTo: "only_you")
        );

        var page = await LibraryPageAssembler.ReadAsync(
            new LibraryPageDependencies(
                repository,
                owners,
                new LibraryOwnerOptions(),
                new FakeSystemClock(DateTime.UtcNow)
            ),
            Tenant,
            LibraryAccessPolicyTests.Access(readable: [LibrarySource.Notes], viewAll: true),
            [LibrarySource.Notes],
            LibraryFilter.Create(new LibrarySearchRequest()).Value,
            CancellationToken.None
        );

        var item = Assert.Single(page.Items);
        Assert.Equal(("note_attachment", "only_you", "Call Ana"), (item.Kind, item.RestrictedTo, item.ResourceTitle));
        Assert.Equal(FileOwnerService.Notes, Assert.Single(owners.Calls).Owner);
    }

    [Fact]
    public async Task A_note_attachment_notes_does_not_confirm_is_not_listed()
    {
        var row = Row(OwnerType.Tenant, FolderType.Other, Guid.NewGuid(), "note", "attachment");
        var repository = new FakeLibraryReadRepository().Add(Tenant, row);

        var page = await LibraryPageAssembler.ReadAsync(
            new LibraryPageDependencies(
                repository,
                new FakeFileOwnerVisibilityClient(),
                new LibraryOwnerOptions(),
                new FakeSystemClock(DateTime.UtcNow)
            ),
            Tenant,
            LibraryAccessPolicyTests.Access(readable: [LibrarySource.Notes], viewAll: true),
            [LibrarySource.Notes],
            LibraryFilter.Create(new LibrarySearchRequest()).Value,
            CancellationToken.None
        );

        Assert.Empty(page.Items);
    }

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
