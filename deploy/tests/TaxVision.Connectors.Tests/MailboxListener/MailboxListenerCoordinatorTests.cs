using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.MailboxListener.Coordination;
using TaxVision.Connectors.MailboxListener.Options;
using TaxVision.Connectors.Tests.OAuth;
using TaxVision.Connectors.Tests.Sync;

namespace TaxVision.Connectors.Tests.MailboxListener;

public sealed class MailboxListenerCoordinatorTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeTenantEmailAccountRepository _repository = new();
    private readonly ControllableSessionFactory _sessions = new();
    private readonly ManualTimeProvider _time = new();

    private TenantEmailAccount AddActive(ProviderCode provider = ProviderCode.Imap)
    {
        var account = TenantEmailAccount
            .Create(Guid.NewGuid(), $"{Guid.NewGuid():N}@example.com", provider, Guid.NewGuid(), Now)
            .Value;
        account.MarkConnected(Now);
        account.Activate(Now);
        _repository.Accounts.Add(account);
        return account;
    }

    private MailboxListenerCoordinator CreateCoordinator(IDistributedLock distributedLock, int maxAccounts = 500)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantEmailAccountRepository>(_repository);
        return new MailboxListenerCoordinator(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            distributedLock,
            _sessions,
            Microsoft.Extensions.Options.Options.Create(
                new MailboxListenerOptions { MaxAccountsPerInstance = maxAccounts }
            ),
            _time,
            NullLogger<MailboxListenerCoordinator>.Instance
        );
    }

    [Fact]
    public async Task Tick_ListensOnlyToActiveImapAccounts()
    {
        var imap = AddActive();
        AddActive(ProviderCode.Gmail);
        var disconnected = AddActive();
        disconnected.Disconnect(Now);
        var coordinator = CreateCoordinator(new InMemoryDistributedLock());

        await coordinator.TickAsync(CancellationToken.None);

        Assert.Equal(1, coordinator.SessionCount);
        Assert.True(coordinator.IsListening(imap.Id));
        await coordinator.StopAllAsync();
    }

    [Fact]
    public async Task Tick_NeverGoesOverTheInstanceLimit()
    {
        AddActive();
        AddActive();
        AddActive();
        var coordinator = CreateCoordinator(new InMemoryDistributedLock(), maxAccounts: 2);

        await coordinator.TickAsync(CancellationToken.None);
        await coordinator.TickAsync(CancellationToken.None);

        Assert.Equal(2, coordinator.SessionCount);
        Assert.Equal(2, _sessions.Created);
        await coordinator.StopAllAsync();
    }

    [Fact]
    public async Task Tick_SkipsAccountsLeasedByAnotherInstance()
    {
        var mine = AddActive();
        var theirs = AddActive();
        var locks = new InMemoryDistributedLock();
        await using var otherInstance = await locks.AcquireAsync(
            MailboxListenerCoordinator.LeaseKey(theirs.Id),
            TimeSpan.FromMinutes(1)
        );
        var coordinator = CreateCoordinator(locks);

        await coordinator.TickAsync(CancellationToken.None);

        Assert.True(coordinator.IsListening(mine.Id));
        Assert.False(coordinator.IsListening(theirs.Id));
        await coordinator.StopAllAsync();
    }

    [Fact]
    public async Task Tick_StopsListeningWhenTheAccountIsNoLongerActive()
    {
        var account = AddActive();
        var locks = new InMemoryDistributedLock();
        var coordinator = CreateCoordinator(locks);
        await coordinator.TickAsync(CancellationToken.None);

        account.Disconnect(Now);
        await coordinator.TickAsync(CancellationToken.None);

        Assert.False(coordinator.IsListening(account.Id));
        Assert.True(_sessions.Sessions[account.Id].WasCancelled);
        // El lease quedó libre: otra instancia (o esta) puede tomarlo.
        await using var lease = await locks.AcquireAsync(
            MailboxListenerCoordinator.LeaseKey(account.Id),
            TimeSpan.FromMinutes(1)
        );
        Assert.True(lease.IsAcquired);
    }

    [Fact]
    public async Task Tick_LostLease_StopsTheSession()
    {
        var account = AddActive();
        var coordinator = CreateCoordinator(new LostOnExtendDistributedLock());
        await coordinator.TickAsync(CancellationToken.None);
        var first = _sessions.Sessions[account.Id];

        // La segunda vuelta renueva primero: el lease ya es de otro, así que se suelta la sesión.
        await coordinator.TickAsync(CancellationToken.None);

        Assert.True(first.WasCancelled);
        await coordinator.StopAllAsync();
    }

    [Fact]
    public async Task SessionThatEndsByItself_WaitsTheCooldownBeforeRetrying()
    {
        var account = AddActive();
        var coordinator = CreateCoordinator(new InMemoryDistributedLock());
        await coordinator.TickAsync(CancellationToken.None);
        _sessions.Sessions[account.Id].End();
        await WaitUntilAsync(async () =>
        {
            await coordinator.TickAsync(CancellationToken.None);
            return !coordinator.IsListening(account.Id);
        });

        await coordinator.TickAsync(CancellationToken.None);
        Assert.Equal(1, _sessions.Created);

        _time.Now += MailboxListenerCoordinator.EndedCooldown;
        await coordinator.TickAsync(CancellationToken.None);
        Assert.Equal(2, _sessions.Created);
        Assert.True(coordinator.IsListening(account.Id));
        await coordinator.StopAllAsync();
    }

    [Fact]
    public async Task StopAll_CancelsEverySessionAndReleasesTheLeases()
    {
        var first = AddActive();
        var second = AddActive();
        var locks = new InMemoryDistributedLock();
        var coordinator = CreateCoordinator(locks);
        await coordinator.TickAsync(CancellationToken.None);

        await coordinator.StopAllAsync();

        Assert.Equal(0, coordinator.SessionCount);
        Assert.All(_sessions.Sessions.Values, s => Assert.True(s.WasCancelled));
        foreach (var id in new[] { first.Id, second.Id })
        {
            await using var lease = await locks.AcquireAsync(
                MailboxListenerCoordinator.LeaseKey(id),
                TimeSpan.FromMinutes(1)
            );
            Assert.True(lease.IsAcquired);
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met in time.");
            await Task.Delay(10);
        }
    }
}
