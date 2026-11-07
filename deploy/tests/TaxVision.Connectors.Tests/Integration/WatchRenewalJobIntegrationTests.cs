using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Domain.Watch;
using TaxVision.Connectors.Infrastructure.Jobs;
using TaxVision.Connectors.Tests.Watch;
using Wolverine;

namespace TaxVision.Connectors.Tests.Integration;

/// <summary>
/// CON-08 contra SQL real: al tercer fallo de renovación, el estado Error y su aviso se confirman juntos o no se
/// confirma ninguno. El proveedor es falso: nunca se llama a Gmail ni a Graph.
/// </summary>
[Collection(ConnectorsJobHost.Collection)]
public sealed class WatchRenewalJobIntegrationTests : IAsyncLifetime
{
    private readonly ConnectorsJobHost _host = new("conwatch");
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FakeWatchProviderClient _provider = new(ProviderCode.Gmail)
    {
        OnRenew = (_, _) => throw new WatchProviderException("Provider rejected the renewal."),
    };

    public Task InitializeAsync() =>
        _host.StartAsync(services =>
            services.AddScoped<IWatchProviderClientFactory>(_ => new FakeWatchProviderClientFactory(_provider))
        );

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact(Timeout = 120_000)]
    public async Task FailureAfterSave_LeavesNoErrorStateWithoutEnvelope()
    {
        var (accountId, subscriptionId) = await SeedAsync();
        _host.FailAfterSave.Armed = true;

        await RunJobAsync();

        Assert.Equal(TenantEmailAccountStatus.Active, await AccountStatusAsync(accountId));
        Assert.Equal(ProviderWatchStatus.Active, await SubscriptionStatusAsync(subscriptionId));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await _host.CountEnvelopesAsync("wolverine_incoming_envelopes", "TenantEmailAccountErrored"));
        Assert.Equal(
            0,
            await _host.CountEnvelopesAsync(
                "wolverine_outgoing_envelopes",
                nameof(ConnectorsWatchExpiredIntegrationEvent)
            )
        );
    }

    [Fact(Timeout = 120_000)]
    public async Task ThirdFailure_ErrorStateAndNoticeCommitTogether()
    {
        var (accountId, subscriptionId) = await SeedAsync();

        await RunJobAsync();

        Assert.Equal(TenantEmailAccountStatus.Error, await AccountStatusAsync(accountId));
        Assert.Equal(ProviderWatchStatus.Failed, await SubscriptionStatusAsync(subscriptionId));
        Assert.Equal(1, await _host.WaitForOutgoingAsync(nameof(ConnectorsWatchExpiredIntegrationEvent)));
    }

    // Dentro de un handler la falla usa la transacción del handler: el evento sale con su commit.
    [Fact(Timeout = 120_000)]
    public async Task ThirdFailureInsideAHandler_UsesItsTransactionAndEmitsOnce()
    {
        var (accountId, subscriptionId) = await SeedAsync();

        await _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync(
                new GraphLifecycleNotificationReceived(
                    "history-1",
                    GraphLifecycleEvents.ReauthorizationRequired,
                    "lifecycle-correlation"
                )
            );

        Assert.Equal(TenantEmailAccountStatus.Error, await AccountStatusAsync(accountId));
        Assert.Equal(ProviderWatchStatus.Failed, await SubscriptionStatusAsync(subscriptionId));
        Assert.Equal(1, await _host.WaitForOutgoingAsync(nameof(ConnectorsWatchExpiredIntegrationEvent)));
    }

    // CON-12: el handler ya no tiene transacción, así que el fallo queda en su unidad, como en el job: no se relanza.
    [Fact(Timeout = 120_000)]
    public async Task HandlerFailsAfterTheFailureWasRecorded_RollsBackStateAndEvent()
    {
        var (accountId, subscriptionId) = await SeedAsync();
        _host.FailAfterSave.Armed = true;

        await _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync(
                new GraphLifecycleNotificationReceived(
                    "history-1",
                    GraphLifecycleEvents.ReauthorizationRequired,
                    "lifecycle-correlation"
                )
            );

        Assert.Equal(TenantEmailAccountStatus.Active, await AccountStatusAsync(accountId));
        Assert.Equal(ProviderWatchStatus.Active, await SubscriptionStatusAsync(subscriptionId));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await _host.CountEnvelopesAsync("wolverine_incoming_envelopes", "TenantEmailAccountErrored"));
    }

    // Sin unidad, la excepción sale del job; con ella queda registrada y el barrido sigue.
    private async Task RunJobAsync()
    {
        try
        {
            await new WatchRenewalJob(
                _host.Services,
                Options.Create(new WatchRenewalOptions()),
                NullLogger<WatchRenewalJob>.Instance
            ).RunOnceAsync(default);
        }
        catch (InvalidOperationException) { }
    }

    private async Task<(Guid AccountId, Guid SubscriptionId)> SeedAsync()
    {
        var now = DateTime.UtcNow;
        var account = TenantEmailAccount
            .Create(_tenantId, $"{Guid.NewGuid():N}@example.test", ProviderCode.Gmail, Guid.NewGuid(), now)
            .Value;
        account.MarkConnected(now);
        account.Activate(now);
        var subscription = ProviderWatchSubscription
            .Create(account.Id, ProviderCode.Gmail, "history-1", "topic", now.AddHours(12), now)
            .Value;
        subscription.RecordRenewalFailure();
        subscription.RecordRenewalFailure();

        await using var db = _host.NewContext();
        db.TenantEmailAccounts.Add(account);
        db.ProviderWatchSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return (account.Id, subscription.Id);
    }

    private async Task<TenantEmailAccountStatus> AccountStatusAsync(Guid accountId)
    {
        await using var db = _host.NewContext();
        return await db
            .TenantEmailAccounts.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => a.Status)
            .SingleAsync();
    }

    private async Task<ProviderWatchStatus> SubscriptionStatusAsync(Guid subscriptionId)
    {
        await using var db = _host.NewContext();
        return await db
            .ProviderWatchSubscriptions.AsNoTracking()
            .Where(s => s.Id == subscriptionId)
            .Select(s => s.Status)
            .SingleAsync();
    }
}
