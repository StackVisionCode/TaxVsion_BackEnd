using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Tests.OAuth;
using TaxVision.Connectors.Tests.Providers;
using TaxVision.Connectors.Tests.Watch;

namespace TaxVision.Connectors.Tests.Accounts;

public class ConnectManualAccountHandlerTests
{
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Fixture(
        FakeTenantEmailAccountRepository AccountRepository,
        FakeImapCredentialsRepository ImapCredentialsRepository,
        FakeSmtpCredentialsRepository SmtpCredentialsRepository,
        FakeManualAccountConnectivityValidator ConnectivityValidator,
        FakeEncryptedSecretProtector Protector,
        FakeProviderConnectionAuditLogRepository AuditLogRepository,
        FakeProviderWatchSubscriptionRepository WatchSubscriptionRepository,
        FakeWatchProviderClientFactory WatchClientFactory,
        FakeUnitOfWork UnitOfWork,
        FakeMessageBus Bus
    );

    private static Fixture CreateFixture() =>
        new(
            new FakeTenantEmailAccountRepository(),
            new FakeImapCredentialsRepository(),
            new FakeSmtpCredentialsRepository(),
            new FakeManualAccountConnectivityValidator(),
            new FakeEncryptedSecretProtector(),
            new FakeProviderConnectionAuditLogRepository(),
            new FakeProviderWatchSubscriptionRepository(),
            // ProviderCode.Imap nunca llega a resolver un watch client (ver WatchActivationService),
            // así que client: null es correcto acá — nunca se invoca.
            new FakeWatchProviderClientFactory(client: null),
            new FakeUnitOfWork(),
            new FakeMessageBus()
        );

    private static ConnectManualAccountCommand ValidCommand(Guid tenantId, Guid userId) =>
        new(
            tenantId,
            userId,
            "office@example.com", // InitiatorEmail — igual al buzón, así el guard de identidad pasa
            "office@example.com",
            "Front Office",
            "imap.example.com",
            993,
            true,
            "imap-user",
            "imap-pass",
            "smtp.example.com",
            587,
            true,
            "smtp-user",
            "smtp-pass"
        );

    private static Task<Result<ConnectManualAccountResult>> HandleAsync(
        Fixture fixture,
        ConnectManualAccountCommand cmd
    ) =>
        ConnectManualAccountHandler.Handle(
            cmd,
            fixture.AccountRepository,
            fixture.ImapCredentialsRepository,
            fixture.SmtpCredentialsRepository,
            fixture.ConnectivityValidator,
            fixture.Protector,
            fixture.AuditLogRepository,
            fixture.WatchSubscriptionRepository,
            fixture.WatchClientFactory,
            fixture.UnitOfWork,
            fixture.Bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

    [Fact]
    public async Task Handle_ValidManualAccount_PersistsImapAndSmtpCredentialsAndPublishesEvent()
    {
        var fixture = CreateFixture();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var result = await HandleAsync(fixture, ValidCommand(tenantId, userId));

        Assert.True(result.IsSuccess);
        Assert.Equal("office@example.com", result.Value.EmailAddress);
        Assert.Single(fixture.AccountRepository.Accounts);
        Assert.Equal(ProviderCode.Imap, fixture.AccountRepository.Accounts[0].ProviderCode);
        // Regresión del bug de producción del 2026-07-24: la cuenta debe quedar Active al final del
        // handler, en el mismo scope/transacción — antes del fix, la activación se despachaba como un
        // SetupWatchCommand nuevo de Wolverine que corría en OTRA transacción y no veía esta cuenta
        // recién insertada, así que el flujo entero fallaba con 404 aunque los INSERT sí committeaban.
        Assert.Equal(TenantEmailAccountStatus.Active, fixture.AccountRepository.Accounts[0].Status);
        Assert.Single(fixture.ImapCredentialsRepository.Credentials);
        Assert.Single(fixture.SmtpCredentialsRepository.Credentials);
        Assert.Equal(
            fixture.AccountRepository.Accounts[0].Id,
            fixture.ImapCredentialsRepository.Credentials[0].AccountId
        );
        Assert.Equal(
            fixture.AccountRepository.Accounts[0].Id,
            fixture.SmtpCredentialsRepository.Credentials[0].AccountId
        );
        // Ya no se despacha SetupWatchCommand vía bus — WatchActivationService corre en proceso.
        Assert.Empty(fixture.Bus.Invoked);
        var published = Assert.Single(fixture.Bus.Published);
        var connectedEvent = Assert.IsType<ConnectorsTenantEmailAccountConnectedIntegrationEvent>(published);
        Assert.Equal("office@example.com", connectedEvent.EmailAddress);
        Assert.Single(fixture.AuditLogRepository.Entries);
    }

    [Fact]
    public async Task Handle_ImapConnectivityFails_ReturnsFailureWithoutPersisting()
    {
        var fixture = CreateFixture();
        fixture.ConnectivityValidator.ImapResult = Result.Failure(
            new Error("ManualAccountConnectivityValidator.ImapFailed", "boom")
        );

        var result = await HandleAsync(fixture, ValidCommand(Guid.NewGuid(), Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("ManualAccountConnectivityValidator.ImapFailed", result.Error.Code);
        Assert.Empty(fixture.AccountRepository.Accounts);
        Assert.Empty(fixture.ImapCredentialsRepository.Credentials);
        Assert.Empty(fixture.SmtpCredentialsRepository.Credentials);
    }

    [Fact]
    public async Task Handle_SmtpConnectivityFails_ReturnsFailureWithoutPersisting()
    {
        var fixture = CreateFixture();
        fixture.ConnectivityValidator.SmtpResult = Result.Failure(
            new Error("ManualAccountConnectivityValidator.SmtpFailed", "boom")
        );

        var result = await HandleAsync(fixture, ValidCommand(Guid.NewGuid(), Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("ManualAccountConnectivityValidator.SmtpFailed", result.Error.Code);
        Assert.Empty(fixture.AccountRepository.Accounts);
    }

    [Fact]
    public async Task Handle_EmailAlreadyConnectedForSameTenant_FailsCleanAlreadyConnected()
    {
        var fixture = CreateFixture();
        var tenantId = Guid.NewGuid();
        var existing = TenantEmailAccount
            .Create(tenantId, "office@example.com", ProviderCode.Imap, Guid.NewGuid(), Now)
            .Value;
        fixture.AccountRepository.Accounts.Add(existing);

        var result = await HandleAsync(fixture, ValidCommand(tenantId, Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("ConnectManualAccountHandler.AlreadyConnected", result.Error.Code);
        Assert.Single(fixture.AccountRepository.Accounts);
    }

    [Fact]
    public async Task Handle_SameEmailInAnotherTenant_CreatesSeparateAccount()
    {
        // Per-tenant (igual que Auth): el mismo buzón en OTRO tenant no bloquea — se crea uno propio.
        var fixture = CreateFixture();
        var thisTenant = Guid.NewGuid();
        var otherTenantAccount = TenantEmailAccount
            .Create(Guid.NewGuid(), "office@example.com", ProviderCode.Imap, Guid.NewGuid(), Now)
            .Value;
        fixture.AccountRepository.Accounts.Add(otherTenantAccount);

        var result = await HandleAsync(fixture, ValidCommand(thisTenant, Guid.NewGuid()));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, fixture.AccountRepository.Accounts.Count);
        Assert.Contains(fixture.AccountRepository.Accounts, a => a.TenantId == thisTenant);
    }

    [Fact]
    public async Task Handle_MailboxDoesNotMatchUserEmail_FailsWithIdentityMismatch()
    {
        // El buzón tecleado debe ser el email de login del usuario (bloquea conectar el de otro).
        var fixture = CreateFixture();
        var cmd = ValidCommand(Guid.NewGuid(), Guid.NewGuid()) with { InitiatorEmail = "someoneelse@office.com" };

        var result = await HandleAsync(fixture, cmd);

        Assert.True(result.IsFailure);
        Assert.Equal("Connectors.EmailIdentity.Mismatch", result.Error.Code);
        Assert.Empty(fixture.AccountRepository.Accounts);
    }
}
