using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using TaxVision.Correspondence.Application.Projections;

namespace TaxVision.Correspondence.Tests.Projections;

public sealed class MailboxOwnershipProjectionConsumerTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeMailboxOwnershipRepository _rows = new();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _account = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();

    private ConnectorsTenantEmailAccountConnectedIntegrationEvent Connected(Guid? owner, bool isOffice, DateTime at) =>
        new()
        {
            TenantId = _tenant,
            CorrelationId = "cor-10",
            AccountId = _account,
            EmailAddress = "mailbox@cor10.test",
            ProviderCode = "Gmail",
            ConnectedAtUtc = at,
            OwnerUserId = owner,
            IsOffice = isOffice,
        };

    private ConnectorsTenantEmailAccountDisconnectedIntegrationEvent Disconnected(Guid? owner, DateTime at) =>
        new()
        {
            TenantId = _tenant,
            CorrelationId = "cor-10",
            AccountId = _account,
            EmailAddress = "mailbox@cor10.test",
            ProviderCode = "Gmail",
            DisconnectedAtUtc = at,
            OwnerUserId = owner,
            IsOffice = owner is null,
        };

    private Task HandleAsync(ConnectorsTenantEmailAccountConnectedIntegrationEvent evt) =>
        MailboxOwnershipProjectionConsumer.Handle(
            evt,
            _rows,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

    private Task HandleAsync(ConnectorsTenantEmailAccountDisconnectedIntegrationEvent evt) =>
        MailboxOwnershipProjectionConsumer.Handle(
            evt,
            _rows,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

    [Fact]
    public async Task Connected_CreatesThePersonalRow()
    {
        await HandleAsync(Connected(_owner, isOffice: false, Now));

        var row = Assert.Single(_rows.Rows);
        Assert.Equal(_owner, row.OwnerUserId);
        Assert.False(row.IsOffice);
        Assert.True(row.IsActive);
    }

    [Fact]
    public async Task Disconnected_MarksInactive()
    {
        await HandleAsync(Connected(_owner, isOffice: false, Now));

        await HandleAsync(Disconnected(_owner, Now.AddMinutes(1)));

        Assert.False(Assert.Single(_rows.Rows).IsActive);
    }

    [Fact]
    public async Task Reconnected_by_another_owner_moves_the_mailbox()
    {
        var newOwner = Guid.NewGuid();
        await HandleAsync(Connected(_owner, isOffice: false, Now));
        await HandleAsync(Disconnected(_owner, Now.AddMinutes(1)));

        await HandleAsync(Connected(newOwner, isOffice: false, Now.AddMinutes(2)));

        var row = Assert.Single(_rows.Rows);
        Assert.Equal(newOwner, row.OwnerUserId);
        Assert.True(row.IsActive);
    }

    [Fact]
    public async Task A_late_event_is_ignored()
    {
        await HandleAsync(Connected(_owner, isOffice: false, Now));

        await HandleAsync(Disconnected(_owner, Now.AddMinutes(-1)));

        Assert.True(Assert.Single(_rows.Rows).IsActive);
    }

    // Un sobre en vuelo durante el despliegue no dice de quién es el buzón: no se inventa una oficina.
    [Fact]
    public async Task An_event_without_owner_fields_creates_nothing()
    {
        await HandleAsync(Connected(owner: null, isOffice: false, Now));

        Assert.Empty(_rows.Rows);
    }

    [Fact]
    public async Task An_event_without_owner_fields_keeps_the_known_owner()
    {
        await HandleAsync(Connected(_owner, isOffice: false, Now));

        await HandleAsync(Connected(owner: null, isOffice: false, Now.AddMinutes(1)));

        Assert.Equal(_owner, Assert.Single(_rows.Rows).OwnerUserId);
    }
}
