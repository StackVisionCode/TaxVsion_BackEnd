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
