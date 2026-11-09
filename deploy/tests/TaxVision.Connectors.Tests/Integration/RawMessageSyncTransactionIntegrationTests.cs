using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Domain.Watch;
using TaxVision.Connectors.Infrastructure.Persistence.Repositories;
using TaxVision.Connectors.Tests.OAuth;
using TaxVision.Connectors.Tests.Sync;
using Wolverine;

namespace TaxVision.Connectors.Tests.Integration;

/// <summary>
/// CON-03 con host real de Wolverine y base temporal: el fetch corre sin transacción abierta y el lock de sync se
/// suelta después del commit. El proveedor es falso: nunca se llama a Gmail ni a Graph.
/// </summary>
[Collection(ConnectorsJobHost.Collection)]
public sealed class RawMessageSyncTransactionIntegrationTests : IAsyncLifetime
{
    // Lleva [MessageIdentity]: el sobre guarda este nombre, no el del tipo.
    private const string RawMessageWireName = "connectors.raw_message_received";

    private readonly ConnectorsJobHost _host = new("consync");
    private readonly FakeEmailProviderClient _provider = new(ProviderCode.Graph);
    private readonly SpyDistributedLock _lock = new();

    private sealed class SpyDistributedLock : IDistributedLock
    {
        private readonly InMemoryDistributedLock _inner = new();

        public Action? OnRelease { get; set; }

        public async Task<ILockHandle> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
            new SpyHandle(await _inner.AcquireAsync(key, ttl, ct), this);

        private sealed class SpyHandle(ILockHandle inner, SpyDistributedLock owner) : ILockHandle
        {
            public bool IsAcquired => inner.IsAcquired;
            public string Key => inner.Key;

            public Task<bool> ExtendAsync(TimeSpan ttl, CancellationToken ct = default) => inner.ExtendAsync(ttl, ct);

            public ValueTask DisposeAsync()
            {
                if (Key.StartsWith("connectors:webhook-sync:", StringComparison.Ordinal))
                    owner.OnRelease?.Invoke();
                return inner.DisposeAsync();
            }
        }
    }

    public Task InitializeAsync() =>
        _host.StartAsync(
            services =>
            {
                services.AddSingleton<IDistributedLock>(_lock);
                services.AddSingleton<ISyncPendingFlag, InMemorySyncPendingFlag>();
                services.AddScoped<IEmailProviderClientFactory>(_ => new FakeEmailProviderClientFactory(_provider));
                services.AddScoped<IProviderSyncCursorRepository, ProviderSyncCursorRepository>();
                services.AddScoped<IProviderMessageFetchFailureRepository, ProviderMessageFetchFailureRepository>();
                services.AddSingleton(MessageFetchFailureLimits.Default);
                services.AddSingleton<ISyncMetrics, FakeSyncMetrics>();
                services.AddScoped<MessageFetchFailureTracker>();
            },
            options =>
            {
                options.Discovery.IncludeType(typeof(ProcessGraphNotificationHandler));
                options.Discovery.IncludeType(typeof(ReconcileAccountHandler));
            }
        );

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact(Timeout = 120_000)]
    public async Task HistoryFetch_RunsWithoutOpenTransaction()
    {
        await SeedAsync();
        int? openDuringFetch = null;
        _provider.OnGetHistory = (_, cursor) =>
        {
            openDuringFetch = _host.Transactions.Open;
            return new HistoryPage(["message-1"], "delta-2", false);
        };

        await InvokeAsync();

        Assert.Equal(0, openDuringFetch);
        Assert.Equal(1, await _host.WaitForOutgoingAsync(RawMessageWireName));
        await AssertCursorAsync("delta-2");
    }

    [Fact(Timeout = 120_000)]
    public async Task SyncLock_ReleasedAfterCommit()
    {
        await SeedAsync();
        _provider.OnGetHistory = (_, _) => new HistoryPage(["message-1"], "delta-2", false);
        int? openAtRelease = null;
        int? committedAtRelease = null;
        _lock.OnRelease = () =>
        {
            openAtRelease = _host.Transactions.Open;
            committedAtRelease = _host.Transactions.Committed;
        };

        await InvokeAsync();

        Assert.Equal(0, openAtRelease);
        Assert.True(committedAtRelease >= 1, "El lock se soltó antes del commit.");
    }

    // Si la unidad falla no queda cursor ni mensaje: la siguiente pasada repite sin perder nada.
    [Fact(Timeout = 120_000)]
    public async Task CommitFails_LeavesNeitherCursorNorMessage()
    {
        await SeedAsync();
        _provider.OnGetHistory = (_, _) => new HistoryPage(["message-1"], "delta-2", false);
        _host.FailAfterSave.Armed = true;

        await Assert.ThrowsAnyAsync<Exception>(InvokeAsync);

        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await _host.CountEnvelopesAsync("wolverine_outgoing_envelopes", RawMessageWireName));
        await AssertCursorAsync(expected: null);
    }

    // Dos pasadas seguidas confirman sobre el mismo DbContext, con la RowVersion ya refrescada.
    [Fact(Timeout = 120_000)]
    public async Task SecondPass_CommitsAfterTheFirst()
    {
        var accountId = await SeedAsync();
        var pass = 0;
        _provider.OnGetHistory = (_, _) => new HistoryPage([$"message-{++pass}"], $"delta-{pass}", false);
        await _host.Services.GetRequiredService<ISyncPendingFlag>().MarkAsync(accountId);

        await InvokeAsync();

        Assert.Equal(2, pass);
        await AssertCursorAsync("delta-2");
        Assert.Equal(2, await WaitForOutgoingCountAsync(2));
    }

    // Sin la transacción del handler, un SaveChanges suelto perdería el evento de dominio.
    [Fact(Timeout = 120_000)]
    public async Task ImapCredentialsRejected_ErrorStateAndNoticeCommitTogether()
    {
        var accountId = await SeedAsync(ProviderCode.Imap);
        _provider.ThrowOnGetHistory = new EmailProviderException("Rejected.", requiresReconnect: true);

        await _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<ReconciliationOutcome>>(new ReconcileAccountCommand(accountId));

        await using var db = _host.NewContext();
        var status = await db
            .TenantEmailAccounts.IgnoreQueryFilters()
            .Where(a => a.Id == accountId)
            .Select(a => a.Status)
            .SingleAsync();
        Assert.Equal(TenantEmailAccountStatus.Error, status);
        Assert.Equal(1, await _host.WaitForOutgoingAsync(nameof(ConnectorsOAuthRefreshFailedIntegrationEvent)));
    }

    private async Task<int> WaitForOutgoingCountAsync(int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var count = 0;
        while (DateTime.UtcNow < deadline && count < expected)
        {
            count = await _host.CountEnvelopesAsync("wolverine_outgoing_envelopes", RawMessageWireName);
            await Task.Delay(250);
        }
        return count;
    }

    private Task InvokeAsync() =>
        _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result>(new ProcessGraphNotificationCommand("subscription-1"));

    private async Task<Guid> SeedAsync(ProviderCode provider = ProviderCode.Graph)
    {
        var now = DateTime.UtcNow;
        var account = TenantEmailAccount
            .Create(Guid.NewGuid(), $"{Guid.NewGuid():N}@example.test", provider, Guid.NewGuid(), now)
            .Value;
        account.MarkConnected(now);
        account.Activate(now);
        var subscription = ProviderWatchSubscription
            .Create(account.Id, ProviderCode.Graph, "subscription-1", "topic", now.AddHours(12), now)
            .Value;
        await using var db = _host.NewContext();
        db.TenantEmailAccounts.Add(account);
        db.ProviderWatchSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private async Task AssertCursorAsync(string? expected)
    {
        await using var db = _host.NewContext();
        var cursor = await db.ProviderSyncCursors.AsNoTracking().SingleOrDefaultAsync();
        Assert.Equal(expected, cursor?.CursorValue);
    }
}
