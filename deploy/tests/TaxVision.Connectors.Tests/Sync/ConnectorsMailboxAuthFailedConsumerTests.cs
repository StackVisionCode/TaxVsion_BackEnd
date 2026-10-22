using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Sync;

public sealed class ConnectorsMailboxAuthFailedConsumerTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeTenantEmailAccountRepository _repository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeMessageBus _bus = new();

    private TenantEmailAccount AddActive(ProviderCode provider)
    {
        var account = TenantEmailAccount
            .Create(Guid.NewGuid(), $"{Guid.NewGuid():N}@example.com", provider, Guid.NewGuid(), Now)
            .Value;
        account.MarkConnected(Now);
        account.Activate(Now);
        _repository.Accounts.Add(account);
        return account;
    }

    private Task HandleAsync(Guid accountId) =>
        ConnectorsMailboxAuthFailedConsumer.Handle(
            new ConnectorsMailboxAuthFailedIntegrationEvent
            {
                TenantId = Guid.NewGuid(),
                CorrelationId = "listener-1",
                AccountId = accountId,
                FailedAtUtc = Now,
            },
            _repository,
            _unitOfWork,
            _bus,
            new FakeCorrelationContext(),
            NullLogger.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task Handle_ImapAccount_IsMarkedAsErrorAndTheUserIsToldOnce()
    {
        var account = AddActive(ProviderCode.Imap);

        await HandleAsync(account.Id);
        await HandleAsync(account.Id);

        Assert.Equal(TenantEmailAccountStatus.Error, account.Status);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
        var notice = Assert.IsType<ConnectorsOAuthRefreshFailedIntegrationEvent>(Assert.Single(_bus.Published));
        Assert.Equal(account.Id, notice.AccountId);
        Assert.Equal("listener-1", notice.CorrelationId);
    }

    [Fact]
    public async Task Handle_NonImapAccount_IsLeftAlone()
    {
        var account = AddActive(ProviderCode.Gmail);

        await HandleAsync(account.Id);

        Assert.Equal(TenantEmailAccountStatus.Active, account.Status);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task Handle_UnknownAccount_DoesNothing()
    {
        await HandleAsync(Guid.NewGuid());

        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_bus.Published);
    }
}
