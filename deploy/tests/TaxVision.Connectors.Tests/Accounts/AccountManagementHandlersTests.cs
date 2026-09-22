using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.OAuth;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Accounts;

public class AccountManagementHandlersTests
{
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task InitiateOAuthConnect_ReturnsAuthorizationUrlContainingAFreshState()
    {
        var providerClient = new FakeOAuthProviderClient(ProviderCode.Gmail);
        var stateStore = new InMemoryOAuthConnectStateStore();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var result = await InitiateOAuthConnectHandler.Handle(
            new InitiateOAuthConnectCommand(tenantId, ProviderCode.Gmail, userId),
            new FakeOAuthProviderClientFactory(providerClient),
            stateStore,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Contains("https://provider.example.com/authorize?state=", result.Value.AuthorizationUrl);

        var state = result.Value.AuthorizationUrl.Split("state=")[1];
        var consumed = await stateStore.ConsumeAsync(state);
        Assert.NotNull(consumed);
        Assert.Equal(tenantId, consumed!.TenantId);
        Assert.Equal(userId, consumed.InitiatedByUserId);
    }

    [Fact]
    public async Task InitiateAdminConsent_ReturnsUrlContainingAFreshState()
    {
        var stateStore = new InMemoryOAuthConnectStateStore();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var result = await InitiateAdminConsentHandler.Handle(
            new InitiateAdminConsentCommand(tenantId, userId),
            new FakeMicrosoftAdminConsentClient(),
            stateStore,
            CancellationToken.None
        );

        Assert.Contains("https://provider.example.com/adminconsent?state=", result.Url);

        var state = result.Url.Split("state=")[1];
        var consumed = await stateStore.ConsumeAsync(state);
        Assert.NotNull(consumed);
        Assert.Equal(tenantId, consumed!.TenantId);
        Assert.Equal(ProviderCode.Graph, consumed.ProviderCode);
    }

    [Fact]
    public async Task ListTenantEmailAccounts_ReturnsOnlyAccountsForThatTenant()
    {
        var repository = new FakeTenantEmailAccountRepository();
        var tenantId = Guid.NewGuid();
        var ownAccount = TenantEmailAccount
            .Create(tenantId, "mine@gmail.com", ProviderCode.Gmail, Guid.NewGuid(), Now)
            .Value;
        var otherAccount = TenantEmailAccount
            .Create(Guid.NewGuid(), "other@gmail.com", ProviderCode.Gmail, Guid.NewGuid(), Now)
            .Value;
        repository.Accounts.Add(ownAccount);
        repository.Accounts.Add(otherAccount);

        var accounts = await ListTenantEmailAccountsHandler.Handle(
            new ListTenantEmailAccountsQuery(tenantId, Guid.NewGuid()),
            repository,
            CancellationToken.None
        );

        // Ambas son de oficina (sin dueño) → la del tenant llamante se ve, la de otro tenant no.
        var dto = Assert.Single(accounts);
        Assert.Equal("mine@gmail.com", dto.EmailAddress);
    }

    [Fact]
    public async Task GetTenantEmailAccount_ForAnotherTenant_ReturnsNotFound()
    {
        var repository = new FakeTenantEmailAccountRepository();
        var account = TenantEmailAccount
            .Create(Guid.NewGuid(), "someone@gmail.com", ProviderCode.Gmail, Guid.NewGuid(), Now)
            .Value;
        repository.Accounts.Add(account);

        var result = await GetTenantEmailAccountHandler.Handle(
            new GetTenantEmailAccountQuery(Guid.NewGuid(), account.Id, Guid.NewGuid()),
            repository,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantEmailAccount.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task GetTenantEmailAccount_PersonalOfAnotherUser_ReturnsNotFound()
    {
        var repository = new FakeTenantEmailAccountRepository();
        var tenantId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var personal = TenantEmailAccount
            .Create(tenantId, "colleague@gmail.com", ProviderCode.Gmail, owner, Now, ownerUserId: owner)
            .Value;
        repository.Accounts.Add(personal);

        var result = await GetTenantEmailAccountHandler.Handle(
            new GetTenantEmailAccountQuery(tenantId, personal.Id, Guid.NewGuid()), // otro usuario
            repository,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantEmailAccount.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task ListVisible_HidesOtherEmployeesPersonalMailboxes()
    {
        var repository = new FakeTenantEmailAccountRepository();
        var tenantId = Guid.NewGuid();
        var me = Guid.NewGuid();
        repository.Accounts.Add(
            TenantEmailAccount.Create(tenantId, "office@taxpro.com", ProviderCode.Gmail, Guid.NewGuid(), Now).Value
        );
        repository.Accounts.Add(
            TenantEmailAccount.Create(tenantId, "me@taxpro.com", ProviderCode.Gmail, me, Now, ownerUserId: me).Value
        );
        repository.Accounts.Add(
            TenantEmailAccount
                .Create(
                    tenantId,
                    "colleague@taxpro.com",
                    ProviderCode.Gmail,
                    Guid.NewGuid(),
                    Now,
                    ownerUserId: Guid.NewGuid()
                )
                .Value
        );

        var accounts = await ListTenantEmailAccountsHandler.Handle(
            new ListTenantEmailAccountsQuery(tenantId, me),
            repository,
            CancellationToken.None
        );

        // Oficina + el mío; NO el personal del colega.
        Assert.Equal(2, accounts.Count);
        Assert.Contains(accounts, a => a.IsOffice);
        Assert.Contains(accounts, a => a.EmailAddress == "me@taxpro.com");
        Assert.DoesNotContain(accounts, a => a.EmailAddress == "colleague@taxpro.com");
    }

    [Fact]
    public async Task ListVisible_WithoutOfficeAccess_HidesOfficeMailbox()
    {
        var repository = new FakeTenantEmailAccountRepository();
        var tenantId = Guid.NewGuid();
        var me = Guid.NewGuid();
        repository.Accounts.Add(
            TenantEmailAccount.Create(tenantId, "office@taxpro.com", ProviderCode.Gmail, Guid.NewGuid(), Now).Value
        );
        repository.Accounts.Add(
            TenantEmailAccount.Create(tenantId, "me@taxpro.com", ProviderCode.Gmail, me, Now, ownerUserId: me).Value
        );

        var accounts = await ListTenantEmailAccountsHandler.Handle(
            new ListTenantEmailAccountsQuery(tenantId, me, CanSeeOffice: false),
            repository,
            CancellationToken.None
        );

        // Sin office.read: solo el propio; la oficina desaparece.
        var dto = Assert.Single(accounts);
        Assert.Equal("me@taxpro.com", dto.EmailAddress);
        Assert.DoesNotContain(accounts, a => a.IsOffice);
    }

    [Fact]
    public async Task GetTenantEmailAccount_OfficeWithoutOfficeAccess_ReturnsNotFound()
    {
        var repository = new FakeTenantEmailAccountRepository();
        var tenantId = Guid.NewGuid();
        var office = TenantEmailAccount
            .Create(tenantId, "office@taxpro.com", ProviderCode.Gmail, Guid.NewGuid(), Now)
            .Value;
        repository.Accounts.Add(office);

        var denied = await GetTenantEmailAccountHandler.Handle(
            new GetTenantEmailAccountQuery(tenantId, office.Id, Guid.NewGuid(), CanSeeOffice: false),
            repository,
            CancellationToken.None
        );
        Assert.True(denied.IsFailure);
        Assert.Equal("TenantEmailAccount.NotFound", denied.Error.Code);

        // Con office.read sí lo ve.
        var allowed = await GetTenantEmailAccountHandler.Handle(
            new GetTenantEmailAccountQuery(tenantId, office.Id, Guid.NewGuid(), CanSeeOffice: true),
            repository,
            CancellationToken.None
        );
        Assert.True(allowed.IsSuccess);
        Assert.True(allowed.Value.IsOffice);
    }

    [Fact]
    public async Task DisconnectAccount_RevokesConnectionAndPublishesEvent()
    {
        var accountRepository = new FakeTenantEmailAccountRepository();
        var connectionRepository = new FakeOAuthConnectionRepository();
        var providerClient = new FakeOAuthProviderClient(ProviderCode.Gmail);
        var protector = new FakeEncryptedSecretProtector();
        var auditLogRepository = new FakeProviderConnectionAuditLogRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var tenantId = Guid.NewGuid();
        var account = TenantEmailAccount
            .Create(tenantId, "office@gmail.com", ProviderCode.Gmail, Guid.NewGuid(), Now)
            .Value;
        account.MarkConnected(Now);
        account.Activate(Now);
        accountRepository.Accounts.Add(account);

        var connection = OAuthConnection.Create(account.Id, ProviderCode.Gmail, "client-1", "scope", Now).Value;
        var token = OAuthToken
            .Create(
                connection.Id,
                protector.Protect("access"),
                protector.Protect("refresh-token"),
                Now.AddHours(1),
                Now
            )
            .Value;
        connection.AttachToken(token);
        connectionRepository.Connections.Add(connection);

        var result = await DisconnectAccountHandler.Handle(
            new DisconnectAccountCommand(tenantId, account.Id),
            accountRepository,
            connectionRepository,
            new FakeOAuthProviderClientFactory(providerClient),
            protector,
            auditLogRepository,
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantEmailAccountStatus.Disconnected, account.Status);
        Assert.Equal(OAuthConnectionStatus.Revoked, connection.Status);
        Assert.Contains("refresh-token", providerClient.RevokedRefreshTokens);
        Assert.Single(auditLogRepository.Entries);
        Assert.Single(bus.Published);
    }
}
