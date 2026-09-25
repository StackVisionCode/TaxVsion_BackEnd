using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Accounts.Consumers;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Tests.OAuth;
using TaxVision.Connectors.Tests.Providers;

namespace TaxVision.Connectors.Tests.Accounts;

/// <summary>
/// Al retirar (offboard) a un empleado: sus buzones PERSONALES se desconectan y se purgan sus
/// secretos; los de OFICINA (y los de otros empleados) no se tocan.
/// </summary>
public sealed class ConnectorsUserOffboardedConsumerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class Harness
    {
        public FakeTenantEmailAccountRepository Accounts { get; } = new();
        public FakeOAuthConnectionRepository OAuth { get; } = new();
        public FakeImapCredentialsRepository Imap { get; } = new();
        public FakeSmtpCredentialsRepository Smtp { get; } = new();
        public FakeOAuthProviderClient ProviderClient { get; } = new(ProviderCode.Gmail);
        public FakeEncryptedSecretProtector Protector { get; } = new();
        public FakeProviderConnectionAuditLogRepository Audit { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public FakeMessageBus Bus { get; } = new();

        public Task Run(Guid tenantId, Guid leaverUserId) =>
            ConnectorsUserOffboardedConsumer.Handle(
                new UserOffboardedIntegrationEvent
                {
                    TenantId = tenantId,
                    UserId = leaverUserId,
                    Email = "leaver@example.com",
                    ActorType = "TenantEmployee",
                    RemovedAtUtc = Now,
                },
                Accounts,
                OAuth,
                Imap,
                Smtp,
                new FakeOAuthProviderClientFactory(ProviderClient),
                Protector,
                Audit,
                UnitOfWork,
                Bus,
                new FakeCorrelationContext(),
                NullLogger<TenantEmailAccount>.Instance,
                CancellationToken.None
            );

        public TenantEmailAccount SeedConnectedMailbox(Guid tenantId, Guid? ownerUserId, string email)
        {
            var account = TenantEmailAccount
                .Create(tenantId, email, ProviderCode.Gmail, Guid.NewGuid(), Now, ownerUserId: ownerUserId)
                .Value;
            account.MarkConnected(Now);
            account.Activate(Now);
            Accounts.Accounts.Add(account);

            var connection = OAuthConnection.Create(account.Id, ProviderCode.Gmail, "client-1", "scope", Now).Value;
            var token = OAuthToken
                .Create(
                    connection.Id,
                    Protector.Protect("access"),
                    Protector.Protect("refresh-token"),
                    Now.AddHours(1),
                    Now
                )
                .Value;
            connection.AttachToken(token);
            OAuth.Connections.Add(connection);

            Imap.Credentials.Add(
                ImapCredentials
                    .Create(account.Id, "imap.gmail.com", 993, true, email, Protector.Protect("imap-pass"))
                    .Value
            );
            Smtp.Credentials.Add(
                SmtpCredentials
                    .Create(account.Id, "smtp.gmail.com", 587, true, email, Protector.Protect("smtp-pass"))
                    .Value
            );
            return account;
        }
    }

    [Fact]
    public async Task Personal_mailbox_is_disconnected_and_its_secrets_purged()
    {
        var tenantId = Guid.NewGuid();
        var leaver = Guid.NewGuid();
        var harness = new Harness();
        var account = harness.SeedConnectedMailbox(tenantId, leaver, "leaver@gmail.com");

        await harness.Run(tenantId, leaver);

        Assert.Equal(TenantEmailAccountStatus.Disconnected, account.Status);
        Assert.Empty(harness.OAuth.Connections); // token + connection purgados
        Assert.Empty(harness.Imap.Credentials);
        Assert.Empty(harness.Smtp.Credentials);
        Assert.Contains("refresh-token", harness.ProviderClient.RevokedRefreshTokens);
        Assert.Single(harness.Bus.Published);
        Assert.Single(harness.Audit.Entries);
    }

    [Fact]
    public async Task Office_and_other_users_mailboxes_are_left_untouched()
    {
        var tenantId = Guid.NewGuid();
        var leaver = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var harness = new Harness();
        var office = harness.SeedConnectedMailbox(tenantId, ownerUserId: null, "office@taxpro.com");
        var others = harness.SeedConnectedMailbox(tenantId, otherUser, "colleague@gmail.com");

        await harness.Run(tenantId, leaver);

        Assert.Equal(TenantEmailAccountStatus.Active, office.Status);
        Assert.Equal(TenantEmailAccountStatus.Active, others.Status);
        Assert.Equal(2, harness.OAuth.Connections.Count);
        Assert.Empty(harness.Bus.Published);
    }

    [Fact]
    public async Task No_personal_mailboxes_is_a_noop()
    {
        var tenantId = Guid.NewGuid();
        var harness = new Harness();

        await harness.Run(tenantId, Guid.NewGuid());

        Assert.Empty(harness.Bus.Published);
        Assert.Equal(0, harness.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Offboarding_impact_counts_the_employees_personal_mailboxes()
    {
        var tenantId = Guid.NewGuid();
        var leaver = Guid.NewGuid();
        var harness = new Harness();
        harness.SeedConnectedMailbox(tenantId, leaver, "leaver@gmail.com");
        harness.SeedConnectedMailbox(tenantId, leaver, "leaver2@gmail.com");
        harness.SeedConnectedMailbox(tenantId, ownerUserId: null, "office@taxpro.com"); // oficina no cuenta
        harness.SeedConnectedMailbox(tenantId, Guid.NewGuid(), "colleague@gmail.com"); // de otro no cuenta

        var result = await OffboardingImpactHandler.Handle(
            new OffboardingImpactQuery(tenantId, leaver),
            harness.Accounts,
            CancellationToken.None
        );

        Assert.Equal(2, result.PersonalMailboxes);
    }
}
