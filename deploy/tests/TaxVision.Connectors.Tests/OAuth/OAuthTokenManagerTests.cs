using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.OAuth;
using TaxVision.Connectors.Infrastructure.RateLimit;

namespace TaxVision.Connectors.Tests.OAuth;

public class OAuthTokenManagerTests
{
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Fixture(
        OAuthTokenManager Manager,
        FakeTenantEmailAccountRepository AccountRepository,
        FakeOAuthConnectionRepository ConnectionRepository,
        FakeOAuthProviderClient ProviderClient,
        FakeUnitOfWork UnitOfWork,
        FakeMessageBus Bus,
        TaxVision.Connectors.Domain.Accounts.TenantEmailAccount Account
    );

    private static Fixture CreateFixture(DateTime accessTokenExpiresAtUtc, InMemoryDistributedLock? sharedLock = null)
    {
        var tenantId = Guid.NewGuid();
        var account = TenantEmailAccount
            .Create(tenantId, "office@gmail.com", ProviderCode.Gmail, Guid.NewGuid(), Now, "Office")
            .Value;
        account.MarkConnected(Now);
        account.Activate(Now);

        var connection = OAuthConnection
            .Create(account.Id, ProviderCode.Gmail, "client-123", "gmail.readonly", Now)
            .Value;
        var protector = new FakeEncryptedSecretProtector();
        var token = OAuthToken
            .Create(
                connection.Id,
                protector.Protect("old-access-token"),
                protector.Protect("old-refresh-token"),
                accessTokenExpiresAtUtc,
                Now
            )
            .Value;
        connection.AttachToken(token);

        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var connectionRepository = new FakeOAuthConnectionRepository();
        connectionRepository.Connections.Add(connection);

        var providerClient = new FakeOAuthProviderClient(ProviderCode.Gmail);
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        var correlation = new FakeCorrelationContext();
        correlation.Set("test-correlation");

        var manager = new OAuthTokenManager(
            accountRepository,
            connectionRepository,
            new FakeOAuthProviderClientFactory(providerClient),
            new ProviderCircuitBreakerRegistry(NullLogger<ProviderCircuitBreakerRegistry>.Instance),
            protector,
            sharedLock ?? new InMemoryDistributedLock(),
            unitOfWork,
            bus,
            correlation,
            NullLogger<OAuthTokenManager>.Instance
        );

        return new Fixture(manager, accountRepository, connectionRepository, providerClient, unitOfWork, bus, account);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_WithFreshToken_ReturnsExistingTokenWithoutCallingProvider()
    {
        // Relativo a DateTime.UtcNow real (no al Now fijo de fixture) — el manager compara contra
        // el reloj real, y Now es una fecha fija que puede ya haber "pasado" al momento del test.
        var fixture = CreateFixture(DateTime.UtcNow.AddHours(1));

        var result = await fixture.Manager.GetValidAccessTokenAsync(fixture.Account.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("old-access-token", result.Value);
        Assert.Equal(0, fixture.ProviderClient.CallCount);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_WithExpiredToken_RefreshesAndReturnsNewToken()
    {
        var fixture = CreateFixture(Now.AddMinutes(-5));

        var result = await fixture.Manager.GetValidAccessTokenAsync(fixture.Account.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-access-token", result.Value);
        Assert.Equal(1, fixture.ProviderClient.CallCount);
        Assert.True(fixture.UnitOfWork.SaveChangesCallCount >= 1);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_WithTokenExpiringWithinBuffer_RefreshesProactively()
    {
        // Expira en 5 minutos — dentro del buffer de 10 minutos, debe refrescar ya.
        var fixture = CreateFixture(Now.AddMinutes(5));

        var result = await fixture.Manager.GetValidAccessTokenAsync(fixture.Account.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fixture.ProviderClient.CallCount);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_RotatesRefreshTokenWhenProviderReturnsOne()
    {
        var fixture = CreateFixture(Now.AddMinutes(-5));
        fixture.ProviderClient.OnRefresh = _ => new TaxVision.Connectors.Application.OAuth.OAuthTokenGrant(
            "new-access-token",
            "new-refresh-token",
            3600
        );

        await fixture.Manager.GetValidAccessTokenAsync(fixture.Account.Id);

        var connection = fixture.ConnectionRepository.Connections[0];
        var protector = new FakeEncryptedSecretProtector();
        Assert.Equal("new-refresh-token", protector.Unprotect(connection.Token!.RefreshTokenCipher));
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_TwoConcurrentCallers_OnlyOneRefreshesProvider()
    {
        var sharedLock = new InMemoryDistributedLock();
        var fixture = CreateFixture(Now.AddMinutes(-5), sharedLock);
        var accountId = fixture.Account.Id;

        var results = await Task.WhenAll(
            fixture.Manager.GetValidAccessTokenAsync(accountId),
            fixture.Manager.GetValidAccessTokenAsync(accountId)
        );

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.All(results, r => Assert.Equal("new-access-token", r.Value));
        Assert.Equal(1, fixture.ProviderClient.CallCount);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_WhenProviderThrows_MarksAccountErrorAndPublishesFailureEvent()
    {
        var fixture = CreateFixture(Now.AddMinutes(-5));
        fixture.ProviderClient.ThrowOnRefresh = new TaxVision.Connectors.Application.OAuth.OAuthProviderException(
            "invalid_grant"
        );

        var result = await fixture.Manager.GetValidAccessTokenAsync(fixture.Account.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(TenantEmailAccountStatus.Error, fixture.Account.Status);

        var published = Assert.Single(fixture.Bus.Published);
        var failedEvent = Assert.IsType<ConnectorsOAuthRefreshFailedIntegrationEvent>(published);
        Assert.Equal(fixture.Account.Id, failedEvent.AccountId);
        Assert.Equal("invalid_grant", failedEvent.Reason);
        Assert.Equal(fixture.Account.CreatedByUserId, failedEvent.CreatedByUserId);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_WithNoConnectionForAccount_ReturnsFailure()
    {
        var fixture = CreateFixture(Now.AddHours(1));

        var result = await fixture.Manager.GetValidAccessTokenAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("OAuthConnection.NotFound", result.Error.Code);
    }
}
