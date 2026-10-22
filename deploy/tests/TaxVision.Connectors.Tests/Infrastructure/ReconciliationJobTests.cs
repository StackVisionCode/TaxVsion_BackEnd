using BuildingBlocks.Common;
using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Jobs;
using TaxVision.Connectors.Tests.MailboxListener;
using TaxVision.Connectors.Tests.OAuth;
using TaxVision.Connectors.Tests.Sync;
using Wolverine;

namespace TaxVision.Connectors.Tests.Infrastructure;

public class ReconciliationJobTests
{
    private const string LeaderLockKey = "connectors:reconciliation:leader";
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    private static TenantEmailAccount CreateActiveAccount(ProviderCode provider = ProviderCode.Imap)
    {
        var account = TenantEmailAccount
            .Create(Guid.NewGuid(), $"{Guid.NewGuid():N}@example.com", provider, Guid.NewGuid(), Now)
            .Value;
        account.MarkConnected(Now);
        account.Activate(Now);
        return account;
    }

    private static ReconciliationJob CreateJob(
        IDistributedLock distributedLock,
        FakeTenantEmailAccountRepository repository,
        FakeMessageBus bus,
        IMailboxListenerPresence? presence = null
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostApplicationLifetime, StartedApplicationLifetime>();
        services.AddSingleton(distributedLock);
        services.AddSingleton(presence ?? new InMemoryMailboxListenerPresence());
        services.AddSingleton<ITenantEmailAccountRepository>(repository);
        services.AddSingleton<IMessageBus>(bus);
        services.AddScoped<ICorrelationContext, FakeCorrelationContext>();
        return new ReconciliationJob(
            services.BuildServiceProvider(),
            Options.Create(new ReconciliationOptions()),
            NullLogger<ReconciliationJob>.Instance
        );
    }

    private static FakeMessageBus BusThatReconcilesNothing() =>
        new()
        {
            InvokeAnyResult = Result.Success(
                new ReconciliationOutcome(MessagesFound: 0, CursorWasSeeded: false, Skipped: false)
            ),
        };

    [Fact]
    public async Task Tick_WhenAnotherInstanceIsLeader_SkipsWithoutReadingAccounts()
    {
        var inner = new InMemoryDistributedLock();
        await using var otherLeader = await inner.AcquireAsync(LeaderLockKey, TimeSpan.FromMinutes(5));
        var signaling = new SignalingDistributedLock(inner);
        var repository = new FakeTenantEmailAccountRepository();
        repository.Accounts.Add(CreateActiveAccount());
        var bus = BusThatReconcilesNothing();
        var job = CreateJob(signaling, repository, bus);

        await job.StartAsync(CancellationToken.None);
        var acquired = await signaling.Attempted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await job.StopAsync(CancellationToken.None);

        Assert.False(acquired);
        Assert.Equal(0, repository.ListActivePageCalls);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Tick_WhenLeaderLockIsLost_StopsBeforeReconcilingAnyAccount()
    {
        var repository = new FakeTenantEmailAccountRepository();
        repository.Accounts.Add(CreateActiveAccount());
        repository.Accounts.Add(CreateActiveAccount());
        var pageRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.OnListActivePage = _ => pageRead.TrySetResult();
        var bus = BusThatReconcilesNothing();
        var job = CreateJob(new LostOnExtendDistributedLock(), repository, bus);

        await job.StartAsync(CancellationToken.None);
        await pageRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await job.StopAsync(CancellationToken.None);

        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Tick_ReconcilesEveryActiveAccountOnce()
    {
        var repository = new FakeTenantEmailAccountRepository();
        var active = new[] { CreateActiveAccount(), CreateActiveAccount(), CreateActiveAccount() };
        repository.Accounts.AddRange(active);
        var disconnected = CreateActiveAccount();
        disconnected.Disconnect(Now);
        repository.Accounts.Add(disconnected);
        var lastPageRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.OnListActivePage = page =>
        {
            if (page.Count == 0)
                lastPageRead.TrySetResult();
        };
        var bus = BusThatReconcilesNothing();
        var job = CreateJob(new InMemoryDistributedLock(), repository, bus);

        await job.StartAsync(CancellationToken.None);
        await lastPageRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await job.StopAsync(CancellationToken.None);

        var reconciled = bus.Invoked.OfType<ReconcileAccountCommand>().Select(c => c.AccountId).Order();
        Assert.Equal(active.Select(a => a.Id).Order(), reconciled);
    }

    [Fact]
    public async Task Tick_SkipsImapAccountsTheListenerIsWatching_ButNotGmail()
    {
        var repository = new FakeTenantEmailAccountRepository();
        var listened = CreateActiveAccount();
        var notListened = CreateActiveAccount();
        var gmail = CreateActiveAccount(ProviderCode.Gmail);
        repository.Accounts.AddRange([listened, notListened, gmail]);
        var presence = new InMemoryMailboxListenerPresence();
        presence.Listen(listened.Id);
        presence.Listen(gmail.Id);
        var bus = BusThatReconcilesNothing();
        var job = CreateJob(new InMemoryDistributedLock(), repository, bus, presence);

        await RunOnePassAsync(job, repository);

        var reconciled = bus.Invoked.OfType<ReconcileAccountCommand>().Select(c => c.AccountId).Order();
        Assert.Equal(new[] { notListened.Id, gmail.Id }.Order(), reconciled);
    }

    [Fact]
    public async Task Tick_ListenedImapAccount_StillGetsTheHourlySafetyNet()
    {
        var repository = new FakeTenantEmailAccountRepository();
        var listened = CreateActiveAccount();
        repository.Accounts.Add(listened);
        var presence = new InMemoryMailboxListenerPresence { SafetyNetSlotFree = true };
        presence.Listen(listened.Id);
        var bus = BusThatReconcilesNothing();
        var job = CreateJob(new InMemoryDistributedLock(), repository, bus, presence);

        await RunOnePassAsync(job, repository);

        var command = Assert.IsType<ReconcileAccountCommand>(Assert.Single(bus.Invoked));
        Assert.Equal(listened.Id, command.AccountId);
    }

    private static async Task RunOnePassAsync(ReconciliationJob job, FakeTenantEmailAccountRepository repository)
    {
        var lastPageRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.OnListActivePage = page =>
        {
            if (page.Count == 0)
                lastPageRead.TrySetResult();
        };
        await job.StartAsync(CancellationToken.None);
        await lastPageRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await job.StopAsync(CancellationToken.None);
    }
}
