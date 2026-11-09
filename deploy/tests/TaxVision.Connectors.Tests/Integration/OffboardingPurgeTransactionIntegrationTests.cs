using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Audit;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Persistence.Repositories;
using TaxVision.Connectors.Tests.OAuth;
using Wolverine;

namespace TaxVision.Connectors.Tests.Integration;

/// <summary>CON-17: la purga del empleado retirado confirma primero y revoca en el proveedor después. Proveedor falso.</summary>
[Collection(ConnectorsJobHost.Collection)]
public sealed class OffboardingPurgeTransactionIntegrationTests : IAsyncLifetime
{
    private readonly ConnectorsJobHost _host = new("conoffb");
    private readonly FakeOAuthProviderClient _oauth = new(ProviderCode.Gmail);

    public Task InitializeAsync() =>
        _host.StartAsync(
            services =>
            {
                services.AddScoped<IOAuthProviderClientFactory>(_ => new FakeOAuthProviderClientFactory(_oauth));
                services.AddScoped<IProviderConnectionAuditLogRepository, ProviderConnectionAuditLogRepository>();
                services.AddScoped<ISmtpCredentialsRepository, SmtpCredentialsRepository>();
                services.AddScoped<IImapCredentialsRepository, ImapCredentialsRepository>();
            },
            options => options.Discovery.IncludeType(typeof(PurgeOffboardedMailboxesHandler))
        );

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact(Timeout = 120_000)]
    public async Task Purge_CommitsBeforeRevokingAtTheProvider()
    {
        var (tenantId, leaver, accountId) = await SeedPersonalMailboxAsync();
        int? openDuringRevoke = null;
        bool? connectionGoneAtRevoke = null;
        _oauth.OnRevoke = () =>
        {
            openDuringRevoke = _host.Transactions.Open;
            using var db = _host.NewContext();
            connectionGoneAtRevoke = !db.OAuthConnections.Any(c => c.AccountId == accountId);
        };

        await _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync(new PurgeOffboardedMailboxesCommand(tenantId, leaver, "corr-offboard"));

        Assert.Equal(0, openDuringRevoke);
        Assert.True(connectionGoneAtRevoke);
        Assert.Equal(["refresh"], _oauth.RevokedRefreshTokens);
        await using var read = _host.NewContext();
        Assert.Equal(
            TenantEmailAccountStatus.Disconnected,
            (await read.TenantEmailAccounts.IgnoreQueryFilters().SingleAsync(a => a.Id == accountId)).Status
        );
        Assert.Equal(
            1,
            await _host.WaitForOutgoingAsync(nameof(ConnectorsTenantEmailAccountDisconnectedIntegrationEvent))
        );
    }

    private async Task<(Guid TenantId, Guid Leaver, Guid AccountId)> SeedPersonalMailboxAsync()
    {
        var now = DateTime.UtcNow;
        var tenantId = Guid.NewGuid();
        var leaver = Guid.NewGuid();
        var protector = new FakeEncryptedSecretProtector();
        var account = TenantEmailAccount
            .Create(tenantId, $"{Guid.NewGuid():N}@example.test", ProviderCode.Gmail, leaver, now, ownerUserId: leaver)
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
        return (tenantId, leaver, account.Id);
    }
}
