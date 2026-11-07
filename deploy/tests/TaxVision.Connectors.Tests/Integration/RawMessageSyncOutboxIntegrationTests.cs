using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
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

/// <summary>Escucha lo que sale de verdad por el puerto: contar sobres da falso verde, porque el revertido sale de memoria.</summary>
public static class ReceivedRawMessageHandler
{
    private static int _count;

    public static int Count => Volatile.Read(ref _count);

    public static void Reset() => Volatile.Write(ref _count, 0);

    public static void Handle(ConnectorsRawMessageReceivedIntegrationEvent _) => Interlocked.Increment(ref _count);
}

/// <summary>CON-03: si la unidad corta falla, el mensaje no sale; al repetir la pasada, sale una sola vez.</summary>
[Collection(ConnectorsJobHost.Collection)]
public sealed class RawMessageSyncOutboxIntegrationTests : IAsyncLifetime
{
    private readonly ConnectorsJobHost _host = new("consyncout");
    private readonly FakeEmailProviderClient _provider = new(ProviderCode.Graph)
    {
        OnGetHistory = (_, _) => new HistoryPage(["message-1"], "delta-2", false),
    };

    public Task InitializeAsync() =>
        _host.StartAsync(
            services =>
            {
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
                options.Discovery.IncludeType(typeof(ReceivedRawMessageHandler));
            },
            receiveNotices: true
        );

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact(Timeout = 120_000)]
    public async Task FailedCommit_SendsNothing_AndTheRepeatedPassSendsOnce()
    {
        ReceivedRawMessageHandler.Reset();
        await SeedAsync();
        _host.FailAfterSave.Armed = true;

        await Assert.ThrowsAnyAsync<Exception>(InvokeAsync);
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(0, ReceivedRawMessageHandler.Count);

        await InvokeAsync();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (ReceivedRawMessageHandler.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(100);
        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Equal(1, ReceivedRawMessageHandler.Count);
    }

    private Task InvokeAsync() =>
        _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result>(new ProcessGraphNotificationCommand("subscription-1"));

    private async Task SeedAsync()
    {
        var now = DateTime.UtcNow;
        var account = TenantEmailAccount
            .Create(Guid.NewGuid(), $"{Guid.NewGuid():N}@example.test", ProviderCode.Graph, Guid.NewGuid(), now)
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
    }
}
