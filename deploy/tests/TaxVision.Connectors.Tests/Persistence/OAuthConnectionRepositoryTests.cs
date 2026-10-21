using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Persistence;
using TaxVision.Connectors.Infrastructure.Persistence.Repositories;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Persistence;

public sealed class OAuthConnectionRepositoryTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    /// <summary>ProactiveTokenRefreshJob corre sin tenant: el repositorio no debe depender del filtro global.</summary>
    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    [Fact]
    public async Task ListAccountIdsWithTokenExpiringBeforeAsync_SkipsAccountsThatCannotRefresh()
    {
        var databaseName = Guid.NewGuid().ToString();
        var active = CreateAccount(account =>
        {
            account.MarkConnected(Now);
            account.Activate(Now);
        });
        var connected = CreateAccount(account => account.MarkConnected(Now));
        var errored = CreateAccount(account =>
        {
            account.MarkConnected(Now);
            account.MarkError(Now);
        });
        var disconnected = CreateAccount(account =>
        {
            account.MarkConnected(Now);
            account.Disconnect(Now);
        });

        await using (var seedDb = CreateContext(databaseName))
        {
            foreach (var account in new[] { active, connected, errored, disconnected })
            {
                await seedDb.TenantEmailAccounts.AddAsync(account);
                await seedDb.OAuthConnections.AddAsync(CreateConnectionWithExpiredToken(account.Id));
            }
            await seedDb.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName);
        var repository = new OAuthConnectionRepository(db);

        var accountIds = await repository.ListAccountIdsWithTokenExpiringBeforeAsync(Now.AddMinutes(30));

        Assert.Equal(new[] { active.Id, connected.Id }.Order(), accountIds.Order());
    }

    private static ConnectorsDbContext CreateContext(string databaseName) =>
        new(
            new DbContextOptionsBuilder<ConnectorsDbContext>().UseInMemoryDatabase(databaseName).Options,
            new NoTenantContext()
        );

    private static TenantEmailAccount CreateAccount(Action<TenantEmailAccount> toStatus)
    {
        var account = TenantEmailAccount
            .Create(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@gmail.com", ProviderCode.Gmail, Guid.NewGuid(), Now)
            .Value;
        toStatus(account);
        return account;
    }

    private static OAuthConnection CreateConnectionWithExpiredToken(Guid accountId)
    {
        var connection = OAuthConnection
            .Create(accountId, ProviderCode.Gmail, "client-123", "gmail.readonly", Now)
            .Value;
        var protector = new FakeEncryptedSecretProtector();
        var token = OAuthToken
            .Create(connection.Id, protector.Protect("access"), protector.Protect("refresh"), Now.AddMinutes(-5), Now)
            .Value;
        connection.AttachToken(token);
        return connection;
    }
}
