using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Postmaster.Application.Projections.ConnectorsEvents;
using TaxVision.Postmaster.Domain.Projections;
using TaxVision.Postmaster.Tests.Providers;

namespace TaxVision.Postmaster.Tests.Consumers;

/// <summary>PMS-04: un aviso viejo no se aplica y no se guarda.</summary>
public sealed class ConnectedMailboxProjectionConsumerTests
{
    private static readonly DateTime ConnectedAt = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _accountId = Guid.NewGuid();
    private readonly FakeConnectedMailboxRepository _repository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private Task SeedAsync() =>
        _repository.AddAsync(
            ConnectedMailbox.ForNewConnection(_tenantId, _accountId, "Gmail", "office@example.test", ConnectedAt)
        );

    // DM-02: un buzón personal se proyecta con su dueño y nunca se usa para correos de la oficina.
    [Fact]
    public async Task A_newer_personal_mailbox_never_replaces_the_office_mailbox()
    {
        var personalId = Guid.NewGuid();
        await ConnectAsync(_accountId, ConnectedAt, ownerUserId: null, isOffice: true);
        await ConnectAsync(personalId, ConnectedAt.AddMinutes(5), ownerUserId: Guid.NewGuid(), isOffice: false);

        var personal = await _repository.GetByAccountIdAsync(_tenantId, personalId);
        Assert.NotNull(personal!.OwnerUserId);
        Assert.False(personal.IsOfficeMailbox);
        Assert.Equal(_accountId, (await _repository.FindActiveByTenantIdAsync(_tenantId))!.AccountId);
    }

    [Fact]
    public async Task An_event_without_ownership_is_not_used_until_it_is_reconciled()
    {
        await ConnectAsync(_accountId, ConnectedAt, ownerUserId: null, isOffice: false);

        var row = await _repository.GetByAccountIdAsync(_tenantId, _accountId);
        Assert.False(row!.OwnershipKnown);
        Assert.Null(await _repository.FindActiveByTenantIdAsync(_tenantId));
    }

    // SR-06: un aviso que se contradice (oficina y con dueño) no se usa hasta reconciliar.
    [Fact]
    public async Task An_event_marked_office_with_an_owner_is_not_used()
    {
        await ConnectAsync(_accountId, ConnectedAt, ownerUserId: Guid.NewGuid(), isOffice: true);

        var row = await _repository.GetByAccountIdAsync(_tenantId, _accountId);
        Assert.False(row!.OwnershipKnown);
        Assert.Null(await _repository.FindActiveByTenantIdAsync(_tenantId));
    }

    [Fact]
    public async Task A_reconnect_without_ownership_keeps_the_known_owner()
    {
        var owner = Guid.NewGuid();
        await ConnectAsync(_accountId, ConnectedAt, ownerUserId: owner, isOffice: false);
        await ConnectAsync(_accountId, ConnectedAt.AddMinutes(1), ownerUserId: null, isOffice: false);

        var row = await _repository.GetByAccountIdAsync(_tenantId, _accountId);
        Assert.Equal(owner, row!.OwnerUserId);
        Assert.True(row.OwnershipKnown);
    }

    private Task ConnectAsync(Guid accountId, DateTime at, Guid? ownerUserId, bool isOffice) =>
        TenantEmailAccountConnectedConsumer.Handle(
            new ConnectorsTenantEmailAccountConnectedIntegrationEvent
            {
                TenantId = _tenantId,
                AccountId = accountId,
                EmailAddress = $"{accountId:N}@example.test",
                ProviderCode = "Gmail",
                ConnectedAtUtc = at,
                OwnerUserId = ownerUserId,
                IsOffice = isOffice,
            },
            _repository,
            _unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<ConnectedMailbox>.Instance,
            CancellationToken.None
        );

    // Lápida, como SCR-12 y SIG-18: el retiro que llega sin fila frena al alta más vieja que venga después.
    [Fact]
    public async Task Disconnect_before_any_connect_leaves_a_tombstone_that_rejects_an_older_connect()
    {
        await TenantEmailAccountDisconnectedConsumer.Handle(
            new ConnectorsTenantEmailAccountDisconnectedIntegrationEvent
            {
                TenantId = _tenantId,
                AccountId = _accountId,
                EmailAddress = "office@example.test",
                ProviderCode = "Gmail",
                DisconnectedAtUtc = ConnectedAt.AddMinutes(5),
            },
            _repository,
            _unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<ConnectedMailbox>.Instance,
            CancellationToken.None
        );
        await TenantEmailAccountConnectedConsumer.Handle(
            new ConnectorsTenantEmailAccountConnectedIntegrationEvent
            {
                TenantId = _tenantId,
                AccountId = _accountId,
                EmailAddress = "office@example.test",
                ProviderCode = "Gmail",
                ConnectedAtUtc = ConnectedAt,
            },
            _repository,
            _unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<ConnectedMailbox>.Instance,
            CancellationToken.None
        );

        var tombstone = await _repository.GetByAccountIdAsync(_tenantId, _accountId);
        Assert.NotNull(tombstone);
        Assert.False(tombstone.IsActive);
        Assert.Equal(ConnectedAt.AddMinutes(5), tombstone.DisconnectedAtUtc);
        Assert.Null(await _repository.FindActiveByTenantIdAsync(_tenantId));
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Older_disconnect_is_ignored_and_not_saved()
    {
        await SeedAsync();

        await TenantEmailAccountDisconnectedConsumer.Handle(
            new ConnectorsTenantEmailAccountDisconnectedIntegrationEvent
            {
                TenantId = _tenantId,
                AccountId = _accountId,
                EmailAddress = "office@example.test",
                ProviderCode = "Gmail",
                DisconnectedAtUtc = ConnectedAt.AddMinutes(-1),
            },
            _repository,
            _unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<ConnectedMailbox>.Instance,
            CancellationToken.None
        );

        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.True((await _repository.GetByAccountIdAsync(_tenantId, _accountId))!.IsActive);
    }

    [Fact]
    public async Task Older_connect_is_ignored_and_not_saved()
    {
        await SeedAsync();
        (await _repository.GetByAccountIdAsync(_tenantId, _accountId))!.MarkDisconnected(ConnectedAt.AddMinutes(5));

        await TenantEmailAccountConnectedConsumer.Handle(
            new ConnectorsTenantEmailAccountConnectedIntegrationEvent
            {
                TenantId = _tenantId,
                AccountId = _accountId,
                EmailAddress = "office@example.test",
                ProviderCode = "Gmail",
                ConnectedAtUtc = ConnectedAt.AddMinutes(1),
            },
            _repository,
            _unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<ConnectedMailbox>.Instance,
            CancellationToken.None
        );

        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.False((await _repository.GetByAccountIdAsync(_tenantId, _accountId))!.IsActive);
    }

    [Fact]
    public async Task Newer_disconnect_is_applied_and_saved()
    {
        await SeedAsync();

        await TenantEmailAccountDisconnectedConsumer.Handle(
            new ConnectorsTenantEmailAccountDisconnectedIntegrationEvent
            {
                TenantId = _tenantId,
                AccountId = _accountId,
                EmailAddress = "office@example.test",
                ProviderCode = "Gmail",
                DisconnectedAtUtc = ConnectedAt.AddMinutes(1),
            },
            _repository,
            _unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<ConnectedMailbox>.Instance,
            CancellationToken.None
        );

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
        Assert.False((await _repository.GetByAccountIdAsync(_tenantId, _accountId))!.IsActive);
    }
}
