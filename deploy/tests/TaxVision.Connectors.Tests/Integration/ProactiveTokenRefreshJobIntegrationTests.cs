using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Domain.Watch;
using TaxVision.Connectors.Infrastructure.Jobs;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Integration;

/// <summary>
/// CON-08 contra SQL real: el refresh rechazado deja la cuenta en Error y el aviso juntos, o ninguno. El proveedor es
/// falso y anota cuántas transacciones había abiertas cuando lo llamaron.
/// </summary>
[Collection(ConnectorsJobHost.Collection)]
public sealed class ProactiveTokenRefreshJobIntegrationTests : IAsyncLifetime
{
    private readonly ConnectorsJobHost _host = new("conrefresh");
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FakeOAuthProviderClient _provider = new(ProviderCode.Gmail);
    private int? _openTransactionsAtProviderCall;

    public Task InitializeAsync()
    {
        _provider.OnRefresh = _ =>
        {
            _openTransactionsAtProviderCall = _host.Transactions.Open;
            throw new OAuthProviderException("invalid_grant", requiresReconnect: true);
        };
        return _host.StartAsync(services =>
            services.AddScoped<IOAuthProviderClientFactory>(_ => new FakeOAuthProviderClientFactory(_provider))
        );
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact(Timeout = 120_000)]
    public async Task FailureAfterSave_LeavesNoErrorStateWithoutEnvelope()
    {
        var accountId = await SeedAsync();
        _host.FailAfterSave.Armed = true;

        await RunJobAsync();

        Assert.Equal(TenantEmailAccountStatus.Active, await AccountStatusAsync(accountId));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await _host.CountEnvelopesAsync("wolverine_incoming_envelopes", "TenantEmailAccountErrored"));
        Assert.Equal(
            0,
            await _host.CountEnvelopesAsync(
                "wolverine_outgoing_envelopes",
                nameof(ConnectorsOAuthRefreshFailedIntegrationEvent)
            )
        );
    }

    [Fact(Timeout = 120_000)]
    public async Task RefreshRejected_ErrorStateAndNoticeCommitTogether()
    {
        var accountId = await SeedAsync();

        await RunJobAsync();

        Assert.Equal(0, _openTransactionsAtProviderCall);
        Assert.Equal(TenantEmailAccountStatus.Error, await AccountStatusAsync(accountId));
        Assert.Equal(1, await _host.WaitForOutgoingAsync(nameof(ConnectorsOAuthRefreshFailedIntegrationEvent)));
        await using var db = _host.NewContext();
        var watch = await db.ProviderWatchSubscriptions.AsNoTracking().SingleAsync(s => s.AccountId == accountId);
        Assert.Equal(ProviderWatchStatus.Failed, watch.Status);
    }

    // Sin unidad, la excepción sale del job; con ella queda registrada y el barrido sigue.
    private async Task RunJobAsync()
    {
        try
        {
            await new ProactiveTokenRefreshJob(
                _host.Services,
                NullLogger<ProactiveTokenRefreshJob>.Instance
            ).RunOnceAsync(default);
        }
        catch (InvalidOperationException) { }
    }

    private async Task<Guid> SeedAsync()
    {
        var now = DateTime.UtcNow;
        var account = TenantEmailAccount
            .Create(_tenantId, $"{Guid.NewGuid():N}@example.test", ProviderCode.Gmail, Guid.NewGuid(), now)
            .Value;
        account.MarkConnected(now);
        account.Activate(now);
        var connection = OAuthConnection.Create(account.Id, ProviderCode.Gmail, "client", "gmail.readonly", now).Value;
        var protector = new FakeEncryptedSecretProtector();
        connection.AttachToken(
            OAuthToken
                .Create(
                    connection.Id,
                    protector.Protect("access"),
                    protector.Protect("refresh"),
                    now.AddMinutes(1),
                    now
                )
                .Value
        );
        var watch = ProviderWatchSubscription
            .Create(account.Id, ProviderCode.Gmail, "history-1", "topic", now.AddDays(7), now)
            .Value;

        await using var db = _host.NewContext();
        db.TenantEmailAccounts.Add(account);
        db.OAuthConnections.Add(connection);
        db.ProviderWatchSubscriptions.Add(watch);
        await db.SaveChangesAsync();
        return account.Id;
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
}
