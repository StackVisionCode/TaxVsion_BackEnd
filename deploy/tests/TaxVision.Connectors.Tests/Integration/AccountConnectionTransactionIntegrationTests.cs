using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Permissions;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Audit;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Persistence.Repositories;
using TaxVision.Connectors.Tests.OAuth;
using TaxVision.Connectors.Tests.Watch;
using Wolverine;

namespace TaxVision.Connectors.Tests.Integration;

/// <summary>CON-03 paso 4: canje, alta del watch y revocación corren sin transacción. Proveedores falsos.</summary>
[Collection(ConnectorsJobHost.Collection)]
public sealed class AccountConnectionTransactionIntegrationTests : IAsyncLifetime
{
    private readonly ConnectorsJobHost _host = new("conaccount");
    private readonly FakeOAuthProviderClient _oauth = new(ProviderCode.Gmail);
    private readonly FakeWatchProviderClient _watch = new(ProviderCode.Gmail);

    public Task InitializeAsync() =>
        _host.StartAsync(
            services =>
            {
                services.AddScoped<IOAuthProviderClientFactory>(_ => new FakeOAuthProviderClientFactory(_oauth));
                services.AddScoped<IWatchProviderClientFactory>(_ => new FakeWatchProviderClientFactory(_watch));
                services.AddScoped<IProviderConnectionAuditLogRepository, ProviderConnectionAuditLogRepository>();
                services.AddScoped<ISmtpCredentialsRepository, SmtpCredentialsRepository>();
                services.AddScoped<IImapCredentialsRepository, ImapCredentialsRepository>();
                services.AddScoped<IProviderSyncCursorRepository, ProviderSyncCursorRepository>();
                services.AddAccountConnectionFlow();
                services.AddSingleton<IUserPermissionsProjectionReader, FakeUserPermissionsProjectionReader>();
            },
            options =>
            {
                options.Discovery.IncludeType(typeof(CompleteOAuthConnectHandler));
                options.Discovery.IncludeType(typeof(DisconnectAccountHandler));
            }
        );

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact(Timeout = 120_000)]
    public async Task CompleteConnect_ProviderCallsRunWithoutOpenTransaction()
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        int? openDuringExchange = null;
        int? openDuringWatch = null;
        _oauth.OnExchange = (_, _) =>
        {
            openDuringExchange = _host.Transactions.Open;
            return new OAuthTokenGrant("access", "refresh", 3600);
        };
        _oauth.OnGetAuthorizedEmailAddress = _ => email;
        _watch.OnSetup = _ =>
        {
            openDuringWatch = _host.Transactions.Open;
            return new WatchSetupResult("history-1", "topic", DateTime.UtcNow.AddDays(7));
        };

        var result = await _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<CompleteOAuthConnectResult>>(
                new CompleteOAuthConnectCommand(Guid.NewGuid(), ProviderCode.Gmail, Guid.NewGuid(), "code", email)
            );

        Assert.True(result.IsSuccess);
        Assert.Equal(0, openDuringExchange);
        Assert.Equal(0, openDuringWatch);
        Assert.Equal(TenantEmailAccountStatus.Active, await StatusAsync(result.Value.AccountId));
        Assert.Equal(
            1,
            await _host.WaitForOutgoingAsync(nameof(ConnectorsTenantEmailAccountConnectedIntegrationEvent))
        );
    }

    // Si el watch falla, la conexión ya quedó guardada: se reintenta sin volver a autorizar.
    [Fact(Timeout = 120_000)]
    public async Task CompleteConnect_WatchFails_KeepsTheConnectionAndLeavesTheAccountDraft()
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        _oauth.OnGetAuthorizedEmailAddress = _ => email;
        _watch.ThrowOnSetup = new WatchProviderException("Provider refused.");

        var result = await _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<CompleteOAuthConnectResult>>(
                new CompleteOAuthConnectCommand(Guid.NewGuid(), ProviderCode.Gmail, Guid.NewGuid(), "code", email)
            );

        Assert.True(result.IsFailure);
        await using var db = _host.NewContext();
        var account = await db.TenantEmailAccounts.IgnoreQueryFilters().SingleAsync(a => a.EmailAddress == email);
        Assert.Equal(TenantEmailAccountStatus.Draft, account.Status);
        Assert.True(await db.OAuthConnections.AnyAsync(c => c.AccountId == account.Id));
    }

    [Fact(Timeout = 120_000)]
    public async Task Disconnect_RevokesAfterTheCommit()
    {
        var (tenantId, accountId) = await SeedConnectedAsync();
        int? openDuringRevoke = null;
        TenantEmailAccountStatus? statusDuringRevoke = null;
        _oauth.OnRevoke = () =>
        {
            openDuringRevoke = _host.Transactions.Open;
            statusDuringRevoke = StatusAsync(accountId).GetAwaiter().GetResult();
        };

        var result = await _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result>(new DisconnectAccountCommand(tenantId, accountId));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, openDuringRevoke);
        Assert.Equal(TenantEmailAccountStatus.Disconnected, statusDuringRevoke);
        Assert.Equal(
            1,
            await _host.WaitForOutgoingAsync(nameof(ConnectorsTenantEmailAccountDisconnectedIntegrationEvent))
        );
    }

    private async Task<TenantEmailAccountStatus> StatusAsync(Guid accountId)
    {
        await using var db = _host.NewContext();
        return await db
            .TenantEmailAccounts.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => a.Status)
            .SingleAsync();
    }

    private async Task<(Guid TenantId, Guid AccountId)> SeedConnectedAsync()
    {
        var now = DateTime.UtcNow;
        var tenantId = Guid.NewGuid();
        var protector = new FakeEncryptedSecretProtector();
        var account = TenantEmailAccount
            .Create(tenantId, $"{Guid.NewGuid():N}@example.test", ProviderCode.Gmail, Guid.NewGuid(), now)
            .Value;
        account.MarkConnected(now);
        account.Activate(now);
        var connection = OAuthConnection.Create(account.Id, ProviderCode.Gmail, "client", "scope", now).Value;
        connection.AttachToken(
            OAuthToken
                .Create(connection.Id, protector.Protect("access"), protector.Protect("refresh"), now.AddHours(1), now)
                .Value
        );
        await using var db = _host.NewContext();
        db.TenantEmailAccounts.Add(account);
        db.OAuthConnections.Add(connection);
        await db.SaveChangesAsync();
        return (tenantId, account.Id);
    }
}
