using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Accounts;

public class CompleteOAuthConnectHandlerTests
{
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Fixture(
        FakeTenantEmailAccountRepository AccountRepository,
        FakeOAuthConnectionRepository ConnectionRepository,
        FakeOAuthProviderClient ProviderClient,
        FakeEncryptedSecretProtector Protector,
        FakeProviderConnectionAuditLogRepository AuditLogRepository,
        FakeUnitOfWork UnitOfWork,
        FakeMessageBus Bus
    );

    private static Fixture CreateFixture() =>
        new(
            new FakeTenantEmailAccountRepository(),
            new FakeOAuthConnectionRepository(),
            new FakeOAuthProviderClient(ProviderCode.Gmail),
            new FakeEncryptedSecretProtector(),
            new FakeProviderConnectionAuditLogRepository(),
            new FakeUnitOfWork(),
            new FakeMessageBus()
        );

    private static Task<Result<CompleteOAuthConnectResult>> HandleAsync(
        Fixture fixture,
        CompleteOAuthConnectCommand cmd
    ) =>
        CompleteOAuthConnectHandler.Handle(
            cmd,
            fixture.AccountRepository,
            fixture.ConnectionRepository,
            new FakeOAuthProviderClientFactory(fixture.ProviderClient),
            fixture.Protector,
            fixture.AuditLogRepository,
            fixture.UnitOfWork,
            fixture.Bus,
            CancellationToken.None
        );

    [Fact]
    public async Task Handle_FirstTimeConnect_CreatesAccountAndConnectionAndPublishesEvent()
    {
        var fixture = CreateFixture();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        fixture.ProviderClient.OnGetAuthorizedEmailAddress = _ => "newoffice@gmail.com";

        var result = await HandleAsync(
            fixture,
            new CompleteOAuthConnectCommand(tenantId, ProviderCode.Gmail, userId, "auth-code")
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("newoffice@gmail.com", result.Value.EmailAddress);
        Assert.Single(fixture.AccountRepository.Accounts);
        Assert.Single(fixture.ConnectionRepository.Connections);
        Assert.NotNull(fixture.ConnectionRepository.Connections[0].Token);
        Assert.Single(fixture.Bus.Invoked);
        Assert.IsType<TaxVision.Connectors.Application.Watch.SetupWatchCommand>(fixture.Bus.Invoked[0]);
        var published = Assert.Single(fixture.Bus.Published);
        var connectedEvent = Assert.IsType<ConnectorsTenantEmailAccountConnectedIntegrationEvent>(published);
        Assert.Equal("newoffice@gmail.com", connectedEvent.EmailAddress);
        Assert.Single(fixture.AuditLogRepository.Entries);
    }

    [Fact]
    public async Task Handle_WatchSetupFails_ReturnsFailureButKeepsPersistedConnection()
    {
        var fixture = CreateFixture();
        fixture.ProviderClient.OnGetAuthorizedEmailAddress = _ => "office@gmail.com";
        fixture.Bus.InvokeResult = Result.Failure(new Error("SetupWatchHandler.ProviderFailed", "boom"));

        var result = await HandleAsync(
            fixture,
            new CompleteOAuthConnectCommand(Guid.NewGuid(), ProviderCode.Gmail, Guid.NewGuid(), "auth-code")
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SetupWatchHandler.ProviderFailed", result.Error.Code);
        Assert.Single(fixture.AccountRepository.Accounts);
        Assert.Single(fixture.ConnectionRepository.Connections);
        Assert.Empty(fixture.Bus.Published);
    }

    [Fact]
    public async Task Handle_MissingRefreshToken_FailsCleanWithoutPersisting()
    {
        var fixture = CreateFixture();
        fixture.ProviderClient.OnExchange = (_, _) => new OAuthTokenGrant("access-token", null, 3600);

        var result = await HandleAsync(
            fixture,
            new CompleteOAuthConnectCommand(Guid.NewGuid(), ProviderCode.Gmail, Guid.NewGuid(), "auth-code")
        );

        Assert.True(result.IsFailure);
        Assert.Equal("CompleteOAuthConnectHandler.MissingRefreshToken", result.Error.Code);
        Assert.Empty(fixture.AccountRepository.Accounts);
        Assert.Empty(fixture.ConnectionRepository.Connections);
    }

    [Fact]
    public async Task Handle_AccountAlreadyHasConnection_FailsCleanAlreadyConnected()
    {
        var fixture = CreateFixture();
        var tenantId = Guid.NewGuid();
        fixture.ProviderClient.OnGetAuthorizedEmailAddress = _ => "office@gmail.com";

        var account = TenantEmailAccount
            .Create(tenantId, "office@gmail.com", ProviderCode.Gmail, Guid.NewGuid(), Now)
            .Value;
        fixture.AccountRepository.Accounts.Add(account);
        var existingConnection = OAuthConnection.Create(account.Id, ProviderCode.Gmail, "client-1", "scope", Now).Value;
        fixture.ConnectionRepository.Connections.Add(existingConnection);

        var result = await HandleAsync(
            fixture,
            new CompleteOAuthConnectCommand(tenantId, ProviderCode.Gmail, Guid.NewGuid(), "auth-code")
        );

        Assert.True(result.IsFailure);
        Assert.Equal("CompleteOAuthConnectHandler.AlreadyConnected", result.Error.Code);
        Assert.Single(fixture.ConnectionRepository.Connections);
    }

    [Fact]
    public async Task Handle_EmailBelongsToAnotherTenant_FailsClean()
    {
        var fixture = CreateFixture();
        fixture.ProviderClient.OnGetAuthorizedEmailAddress = _ => "shared@gmail.com";

        var otherTenantAccount = TenantEmailAccount
            .Create(Guid.NewGuid(), "shared@gmail.com", ProviderCode.Gmail, Guid.NewGuid(), Now)
            .Value;
        fixture.AccountRepository.Accounts.Add(otherTenantAccount);

        var result = await HandleAsync(
            fixture,
            new CompleteOAuthConnectCommand(Guid.NewGuid(), ProviderCode.Gmail, Guid.NewGuid(), "auth-code")
        );

        Assert.True(result.IsFailure);
        Assert.Equal("CompleteOAuthConnectHandler.EmailBelongsToAnotherTenant", result.Error.Code);
    }

    [Fact]
    public async Task Handle_ProviderThrowsOnExchange_ReturnsProviderFailedWithoutPersisting()
    {
        var fixture = CreateFixture();
        fixture.ProviderClient.ThrowOnExchange = new OAuthProviderException("token endpoint returned 400");

        var result = await HandleAsync(
            fixture,
            new CompleteOAuthConnectCommand(Guid.NewGuid(), ProviderCode.Gmail, Guid.NewGuid(), "bad-code")
        );

        Assert.True(result.IsFailure);
        Assert.Equal("CompleteOAuthConnectHandler.ProviderFailed", result.Error.Code);
        Assert.Empty(fixture.AccountRepository.Accounts);
    }

    [Fact]
    public async Task Handle_ReconnectingExistingDisconnectedAccount_ReusesAccountWithoutCreatingDuplicate()
    {
        var fixture = CreateFixture();
        var tenantId = Guid.NewGuid();
        fixture.ProviderClient.OnGetAuthorizedEmailAddress = _ => "office@gmail.com";

        var account = TenantEmailAccount
            .Create(tenantId, "office@gmail.com", ProviderCode.Gmail, Guid.NewGuid(), Now)
            .Value;
        account.MarkConnected(Now);
        account.Activate(Now);
        account.Disconnect(Now);
        fixture.AccountRepository.Accounts.Add(account);

        var result = await HandleAsync(
            fixture,
            new CompleteOAuthConnectCommand(tenantId, ProviderCode.Gmail, Guid.NewGuid(), "auth-code")
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(account.Id, result.Value.AccountId);
        Assert.Single(fixture.AccountRepository.Accounts);
        Assert.Single(fixture.ConnectionRepository.Connections);
    }

    [Fact]
    public async Task Handle_ReconnectingAccountWithRevokedConnection_ReusesTheSameConnectionAndTokenRows()
    {
        var fixture = CreateFixture();
        var tenantId = Guid.NewGuid();
        fixture.ProviderClient.OnGetAuthorizedEmailAddress = _ => "office@gmail.com";
        fixture.ProviderClient.OnExchange = (_, _) =>
            new OAuthTokenGrant("new-access-token", "new-refresh-token", 3600);

        var account = TenantEmailAccount
            .Create(tenantId, "office@gmail.com", ProviderCode.Gmail, Guid.NewGuid(), Now)
            .Value;
        account.MarkConnected(Now);
        account.Activate(Now);
        account.Disconnect(Now);
        fixture.AccountRepository.Accounts.Add(account);

        var connection = OAuthConnection
            .Create(account.Id, ProviderCode.Gmail, "old-client-id", "old-scope", Now)
            .Value;
        var token = OAuthToken
            .Create(
                connection.Id,
                fixture.Protector.Protect("old-access"),
                fixture.Protector.Protect("old-refresh"),
                Now.AddHours(1),
                Now
            )
            .Value;
        connection.AttachToken(token);
        connection.Revoke(Now);
        fixture.ConnectionRepository.Connections.Add(connection);

        var result = await HandleAsync(
            fixture,
            new CompleteOAuthConnectCommand(tenantId, ProviderCode.Gmail, Guid.NewGuid(), "auth-code")
        );

        Assert.True(result.IsSuccess);
        var persistedConnection = Assert.Single(fixture.ConnectionRepository.Connections);
        Assert.Equal(connection.Id, persistedConnection.Id);
        Assert.Equal(OAuthConnectionStatus.Active, persistedConnection.Status);
        Assert.Equal("fake-client-id", persistedConnection.ClientId);
        Assert.NotNull(persistedConnection.Token);
        Assert.Equal(token.Id, persistedConnection.Token!.Id);
        Assert.Equal("new-refresh-token", fixture.Protector.Unprotect(persistedConnection.Token.RefreshTokenCipher));
    }
}
