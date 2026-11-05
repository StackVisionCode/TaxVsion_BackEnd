using BuildingBlocks.Messaging.EmailIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Domain.Sync;
using TaxVision.Connectors.Tests.OAuth;
using TaxVision.Connectors.Tests.Watch;

namespace TaxVision.Connectors.Tests.Sync;

/// <summary>El orquestador es interno: se ejerce por ReconcileAccountHandler, que lo llama tal cual.</summary>
public class RawMessageSyncOrchestratorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private static TenantEmailAccount CreateActiveAccount()
    {
        var account = TenantEmailAccount
            .Create(TenantId, "office@gmail.com", ProviderCode.Gmail, Guid.NewGuid(), Now)
            .Value;
        account.MarkConnected(Now);
        account.Activate(Now);
        return account;
    }

    private sealed record Scenario(
        TenantEmailAccount Account,
        FakeTenantEmailAccountRepository Accounts,
        FakeProviderSyncCursorRepository Cursors,
        FakeEmailProviderClient Client,
        FakeMessageBus Bus,
        FakeProviderMessageFetchFailureRepository Failures,
        FakeSyncMetrics Metrics
    );

    private static Scenario ThreeMessagesFromCursor100()
    {
        var account = CreateActiveAccount();
        var accounts = new FakeTenantEmailAccountRepository();
        accounts.Accounts.Add(account);
        var cursors = new FakeProviderSyncCursorRepository();
        cursors.Cursors.Add(ProviderSyncCursor.Create(account.Id, "100", Now).Value);
        var client = new FakeEmailProviderClient(ProviderCode.Gmail)
        {
            OnGetHistory = (_, _) => new HistoryPage(["m1", "m2", "m3"], "200", false),
        };
        return new Scenario(
            account,
            accounts,
            cursors,
            client,
            new FakeMessageBus(),
            new FakeProviderMessageFetchFailureRepository(),
            new FakeSyncMetrics()
        );
    }

    private static Task<BuildingBlocks.Results.Result<ReconciliationOutcome>> RunAsync(Scenario s) =>
        ReconcileAccountHandler.Handle(
            new ReconcileAccountCommand(s.Account.Id),
            s.Accounts,
            new FakeProviderWatchSubscriptionRepository(),
            s.Cursors,
            new FakeEmailProviderClientFactory(s.Client),
            new InMemoryDistributedLock(),
            new InMemorySyncPendingFlag(),
            new FakeUnitOfWork(),
            s.Bus,
            new FakeCorrelationContext(),
            NullLogger.Instance,
            FetchFailures.Tracker(s.Failures, s.Metrics),
            CancellationToken.None
        );

    private static List<string> PublishedIds(FakeMessageBus bus) =>
        bus.Published.OfType<ConnectorsRawMessageReceivedIntegrationEvent>().Select(e => e.ProviderMessageId).ToList();

    [Fact]
    public async Task Sync_OneMessageTransientFailure_DoesNotAdvanceCursor()
    {
        var s = ThreeMessagesFromCursor100();
        s.Client.ThrowOnGetMessageFor.Add("m2");

        var result = await RunAsync(s);

        Assert.Equal("100", s.Cursors.Cursors[0].CursorValue);
        Assert.Equal(["m1", "m3"], PublishedIds(s.Bus));
        Assert.True(result.IsFailure);
        Assert.Equal("RawMessageSync.MessageFetchPartiallyFailed", result.Error.Code);
    }

    [Fact]
    public async Task Sync_MessageNotFound_SkipsAndAdvancesCursor()
    {
        var s = ThreeMessagesFromCursor100();
        s.Client.NotFoundOnGetMessageFor.Add("m2");

        var result = await RunAsync(s);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.MessagesFound);
        Assert.Equal("200", s.Cursors.Cursors[0].CursorValue);
        Assert.Equal(["m1", "m3"], PublishedIds(s.Bus));
        Assert.Empty(s.Failures.Failures);
    }

    [Fact]
    public async Task Sync_PoisonMessageBeyondAttemptsAndAge_IsAbandonedAndCursorAdvances()
    {
        var s = ThreeMessagesFromCursor100();
        var nowUtc = DateTime.UtcNow;
        var poison = ProviderMessageFetchFailure.Create(s.Account.Id, "m2", "HTTP 500", nowUtc.AddHours(-25)).Value;
        for (var attempt = 2; attempt <= 10; attempt++)
            poison.RegisterFailure("HTTP 500", nowUtc.AddHours(-1));
        s.Failures.Failures.Add(poison);
        s.Client.ThrowOnGetMessageFor.Add("m2");

        var result = await RunAsync(s);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProviderMessageFetchFailureStatus.Abandoned, poison.Status);
        Assert.Equal("200", s.Cursors.Cursors[0].CursorValue);
        Assert.Equal(1, s.Metrics.Abandoned);
    }

    [Fact]
    public async Task Sync_TransientFailure_RecordsPendingFailureThatALaterSuccessResolves()
    {
        var s = ThreeMessagesFromCursor100();
        s.Client.ThrowOnGetMessageFor.Add("m2");

        await RunAsync(s);
        var failure = Assert.Single(s.Failures.Failures);
        Assert.Equal(ProviderMessageFetchFailureStatus.Pending, failure.Status);
        Assert.Equal(1, failure.Attempts);

        s.Client.ThrowOnGetMessageFor.Clear();
        var retry = await RunAsync(s);

        Assert.True(retry.IsSuccess);
        Assert.Equal(ProviderMessageFetchFailureStatus.Resolved, failure.Status);
        Assert.Equal("200", s.Cursors.Cursors[0].CursorValue);
    }
}
