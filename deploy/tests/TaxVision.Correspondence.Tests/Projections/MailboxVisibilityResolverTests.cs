using System.Security.Claims;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Api.Authorization;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Tests.Messages;

namespace TaxVision.Correspondence.Tests.Projections;

public sealed class MailboxVisibilityResolverTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeMailboxOwnershipRepository _rows = new();
    private readonly FakeConnectorsClient _connectors = new();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();

    private MailboxVisibilityResolver Resolver(bool canSeeOffice = false) =>
        Resolver(canSeeOffice ? ConnectorsPermissions.AccountsOfficeRead : null);

    private MailboxVisibilityResolver Resolver(string? permission) =>
        new(new FixedPermissions(permission), _connectors, _rows, NullLogger<MailboxVisibilityResolver>.Instance);

    private ClaimsPrincipal User =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, _user.ToString())], "test"));

    private Guid Seed(Guid? owner, bool isActive = true)
    {
        var account = Guid.NewGuid();
        _rows.Rows.Add(MailboxOwnership.Create(_tenant, account, owner, isActive, Now));
        return account;
    }

    [Fact]
    public async Task Resolve_WithProjection_DoesNotCallConnectors()
    {
        var first = Seed(_user);
        var second = Seed(_user);
        Seed(Guid.NewGuid());
        Seed(owner: null);

        var visible = await Resolver().ResolveVisibleAccountIdsAsync(User, _tenant);

        Assert.Equivalent(new[] { first, second }, visible);
        Assert.Equal(0, _connectors.VisibleCalls);
    }

    [Fact]
    public async Task Resolve_UserWithoutMailboxes_SeesNothingWithoutCallingConnectors()
    {
        Seed(owner: null);

        var visible = await Resolver().ResolveVisibleAccountIdsAsync(User, _tenant);

        Assert.Empty(visible!);
        Assert.Equal(0, _connectors.VisibleCalls);
    }

    // Connectors devuelve los del dueño en cualquier estado: desconectar no esconde el correo ya recibido.
    [Fact]
    public async Task Resolve_DisconnectedMailbox_StaysVisible()
    {
        var disconnected = Seed(_user, isActive: false);

        var visible = await Resolver().ResolveVisibleAccountIdsAsync(User, _tenant);

        Assert.Equal([disconnected], visible);
    }

    [Fact]
    public async Task Resolve_EmptyProjection_FallsBackToConnectors()
    {
        var account = Guid.NewGuid();
        _connectors.VisibleAccountIdsResponse = Result.Success<IReadOnlyCollection<Guid>>([account]);

        var visible = await Resolver().ResolveVisibleAccountIdsAsync(User, _tenant);

        Assert.Equal([account], visible);
        Assert.Equal(1, _connectors.VisibleCalls);
    }

    [Fact]
    public async Task Resolve_EmptyProjection_AndConnectorsDown_HidesEverything()
    {
        _connectors.VisibleAccountIdsResponse = Result.Failure<IReadOnlyCollection<Guid>>(
            new Error("ConnectorsClient.Unavailable", "down")
        );

        var visible = await Resolver().ResolveVisibleAccountIdsAsync(User, _tenant);

        Assert.Empty(visible!);
    }

    // E-2: office.read abre los buzones de oficina, nunca el personal de otro empleado.
    [Theory]
    [InlineData(ConnectorsPermissions.AccountsOfficeRead)]
    [InlineData(ConnectorsPermissions.AccountsWrite)]
    public async Task Resolve_OfficeReader_SeesOwnAndOfficeButNotAnotherPersonal(string permission)
    {
        var mine = Seed(_user);
        var office = Seed(owner: null);
        var disconnectedOffice = Seed(owner: null, isActive: false);
        Seed(Guid.NewGuid());

        var visible = await Resolver(permission).ResolveVisibleAccountIdsAsync(User, _tenant);

        Assert.NotNull(visible);
        Assert.Equivalent(new[] { mine, office, disconnectedOffice }, visible, strict: true);
        Assert.Equal(0, _connectors.VisibleCalls);
    }

    [Fact]
    public async Task Resolve_OfficeReader_EmptyProjection_AsksConnectorsWithOffice()
    {
        var personal = Guid.NewGuid();
        var office = Guid.NewGuid();
        _connectors.VisibleAccountIdsByIncludeOffice = includeOffice =>
            Result.Success<IReadOnlyCollection<Guid>>(includeOffice ? [personal, office] : [personal]);

        var visible = await Resolver(canSeeOffice: true).ResolveVisibleAccountIdsAsync(User, _tenant);

        Assert.Equivalent(new[] { personal, office }, visible, strict: true);
        Assert.Equal(1, _connectors.VisibleCalls);
    }

    [Fact]
    public async Task Resolve_OfficeReader_EmptyProjection_AndConnectorsDown_SeesNothing()
    {
        _connectors.VisibleAccountIdsResponse = Result.Failure<IReadOnlyCollection<Guid>>(
            new Error("ConnectorsClient.Unavailable", "down")
        );

        var visible = await Resolver(canSeeOffice: true).ResolveVisibleAccountIdsAsync(User, _tenant);

        Assert.NotNull(visible);
        Assert.Empty(visible);
    }

    [Fact]
    public async Task Resolve_OfficeReader_WithoutUserId_SeesNothing()
    {
        Seed(owner: null);

        var visible = await Resolver(canSeeOffice: true)
            .ResolveVisibleAccountIdsAsync(new ClaimsPrincipal(new ClaimsIdentity()), _tenant);

        Assert.NotNull(visible);
        Assert.Empty(visible);
    }

    private sealed class FixedPermissions(string? granted) : IUserPermissionsSource
    {
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default) =>
            Task.FromResult(granted is not null && permission == granted);
    }
}
