using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.ActivateTenantPaymentConfig;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.DeleteTenantPaymentConfig;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.RegisterTenantPaymentWebhook;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.UpdateTenantPaymentConfigSecrets;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Queries;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;
using TaxVision.PaymentClient.Infrastructure.Providers;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>Registro automático del webhook: se encola al guardar claves (después de guardar), el comando
/// asíncrono lo crea y guarda solo el secreto, nunca pisa uno manual, Connect se salta, un fallo no rompe
/// nada y al borrar el config se borra el webhook.</summary>
public sealed class WebhookAutoSetupTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly PaymentClientDbContext _db;
    private readonly RegisteringProvider _stripe = new();
    private readonly IPaymentAdapterFactory _adapters;
    private readonly List<object> _published = [];
    private PaymentWebhookOptions _options = new() { PublicBaseUrl = "https://api.example.test" };

    public WebhookAutoSetupTests()
    {
        _db = InMemoryPaymentClient.CreateDb(_databaseName);
        var services = new ServiceCollection().AddKeyedSingleton<IPaymentProvider>(PaymentProviderCode.Stripe, _stripe);
        _adapters = new KeyedPaymentAdapterFactory(services.BuildServiceProvider(), [PaymentProviderCode.Stripe]);
    }

    [Fact]
    public async Task Saving_keys_without_a_webhook_secret_queues_the_registration_after_saving()
    {
        await SeedConfigAsync();

        var result = await SaveSecretsAsync("sk_live_1", webhookSecret: null);

        Assert.True(result.IsSuccess);
        var command = Assert.IsType<RegisterTenantPaymentWebhookCommand>(Assert.Single(_published));
        Assert.Equal(new RegisterTenantPaymentWebhookCommand(TenantId, PaymentProviderCode.Stripe), command);
        var saved = await ReloadAsync();
        Assert.Equal(WebhookSetupStatus.Pending, saved.WebhookSetup);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task The_queued_command_creates_the_webhook_and_stores_only_its_secret()
    {
        await SeedConfigAsync();
        await SaveSecretsAsync("sk_live_1", webhookSecret: null);

        await RegisterAsync();

        var saved = await ReloadAsync();
        Assert.Equal(WebhookSetupStatus.Automatic, saved.WebhookSetup);
        Assert.Equal("we_1", saved.ProviderWebhookId);
        Assert.Equal("enc:whsec_auto", saved.WebhookSecretEncrypted?.CipherText);
        var (credentials, request) = Assert.Single(_stripe.Registrations);
        Assert.Equal("sk_live_1", credentials.SecretKey);
        Assert.Equal(new Uri($"https://api.example.test/{PaymentWebhookRoutes.Stripe}/{TenantId}"), request.Url);
        Assert.Equal(RegisteringProvider.Events, request.Events);
    }

    [Fact]
    public async Task A_pasted_webhook_secret_is_Manual_and_never_queues_the_registration()
    {
        await SeedConfigAsync();

        await SaveSecretsAsync("sk_live_1", "whsec_manual");
        await SaveSecretsAsync("sk_live_2", webhookSecret: null);

        Assert.Empty(_published);
        var saved = await ReloadAsync();
        Assert.Equal(WebhookSetupStatus.Manual, saved.WebhookSetup);
        Assert.Equal("enc:whsec_manual", saved.WebhookSecretEncrypted?.CipherText);
    }

    [Fact]
    public async Task A_manual_secret_after_an_automatic_one_deletes_the_automatic_webhook()
    {
        await SeedConfigAsync();
        await SaveSecretsAsync("sk_live_1", webhookSecret: null);
        await RegisterAsync();

        await SaveSecretsAsync("sk_live_1", "whsec_manual");

        Assert.Equal(["we_1"], _stripe.Deleted);
        var saved = await ReloadAsync();
        Assert.Equal(WebhookSetupStatus.Manual, saved.WebhookSetup);
        Assert.Null(saved.ProviderWebhookId);
    }

    [Fact]
    public async Task A_manual_secret_that_arrives_before_the_command_runs_wins_and_the_new_webhook_is_removed()
    {
        await SeedConfigAsync();
        await SaveSecretsAsync("sk_live_1", webhookSecret: null);
        await SaveSecretsAsync("sk_live_1", "whsec_manual");

        // La fila ya no está pendiente: el comando no hace nada.
        await RegisterAsync();

        Assert.Empty(_stripe.Registrations);
        Assert.Equal(WebhookSetupStatus.Manual, (await ReloadAsync()).WebhookSetup);
    }

    [Fact]
    public async Task Changing_the_key_re_registers_an_automatic_webhook_but_the_same_key_does_not()
    {
        await SeedConfigAsync();
        await SaveSecretsAsync("sk_live_1", webhookSecret: null);
        await RegisterAsync();
        _published.Clear();

        await SaveSecretsAsync("sk_live_1", webhookSecret: null);
        Assert.Empty(_published);

        await SaveSecretsAsync("sk_live_other_account", webhookSecret: null);
        Assert.IsType<RegisterTenantPaymentWebhookCommand>(Assert.Single(_published));
    }

    [Fact]
    public async Task A_transient_failure_is_retried_and_after_the_last_attempt_it_is_Failed()
    {
        _options = new PaymentWebhookOptions { PublicBaseUrl = "https://api.example.test", MaxAttempts = 2 };
        _stripe.Next = () =>
            Result.Failure<RegisteredWebhook>(new Error(WebhookRegistrationErrors.TransientCode, "Stripe is down"));
        await SeedConfigAsync();
        await SaveSecretsAsync("sk_live_1", webhookSecret: null);
        _published.Clear();

        await RegisterAsync(attempt: 1);
        var retry = Assert.IsType<RegisterTenantPaymentWebhookCommand>(Assert.Single(_published));
        Assert.Equal(2, retry.Attempt);
        Assert.Equal(WebhookSetupStatus.Pending, (await ReloadAsync()).WebhookSetup);

        await RegisterAsync(attempt: 2);
        var saved = await ReloadAsync();
        Assert.Equal(WebhookSetupStatus.Failed, saved.WebhookSetup);
        Assert.Null(saved.WebhookSecretEncrypted);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task A_permanent_failure_is_Failed_right_away()
    {
        _stripe.Next = () =>
            Result.Failure<RegisteredWebhook>(new Error("Stripe.Webhook.invalid_request_error", "restricted key"));
        await SeedConfigAsync();
        await SaveSecretsAsync("sk_live_1", webhookSecret: null);
        _published.Clear();

        await RegisterAsync();

        Assert.Empty(_published);
        Assert.Equal(WebhookSetupStatus.Failed, (await ReloadAsync()).WebhookSetup);
    }

    [Fact]
    public async Task A_failed_setup_is_retried_when_the_office_activates_again()
    {
        _stripe.Next = () => Result.Failure<RegisteredWebhook>(new Error("Stripe.Webhook.failed", "no"));
        await SeedConfigAsync();
        await SaveSecretsAsync("sk_live_1", webhookSecret: null);
        await RegisterAsync();
        _published.Clear();

        var activated = await ActivateAsync();

        Assert.True(activated.IsSuccess);
        Assert.IsType<RegisterTenantPaymentWebhookCommand>(Assert.Single(_published));
        Assert.Equal(WebhookSetupStatus.Pending, (await ReloadAsync()).WebhookSetup);
    }

    [Fact]
    public async Task Without_a_public_base_url_nothing_is_queued()
    {
        _options = new PaymentWebhookOptions();
        await SeedConfigAsync();

        await SaveSecretsAsync("sk_live_1", webhookSecret: null);

        Assert.Empty(_published);
        Assert.Null((await ReloadAsync()).WebhookSetup);
    }

    [Fact]
    public async Task A_Connect_config_is_skipped()
    {
        var connect = TenantPaymentConfig
            .Create(
                TenantId,
                PaymentProviderCode.Stripe,
                TenantPaymentMode.Connect,
                "pk_platform",
                StatementDescriptor.Create("ACME TAX SVC").Value,
                DateTime.UtcNow
            )
            .Value;
        _db.TenantPaymentConfigs.Add(connect);
        await _db.SaveChangesAsync();

        var activated = await ActivateAsync();

        Assert.True(activated.IsSuccess);
        Assert.Empty(_published);
        Assert.Null((await ReloadAsync()).WebhookSetup);
    }

    [Fact]
    public async Task Deleting_the_config_deletes_the_automatic_webhook_and_a_failure_does_not_block_it()
    {
        await SeedConfigAsync();
        await SaveSecretsAsync("sk_live_1", webhookSecret: null);
        await RegisterAsync();
        _stripe.DeleteResult = Result.Failure(new Error(WebhookRegistrationErrors.TransientCode, "down"));

        var deleted = await DeleteTenantPaymentConfigHandler.Handle(
            new DeleteTenantPaymentConfigCommand(TenantId, PaymentProviderCode.Stripe, ActorId),
            new TenantPaymentConfigRepository(_db),
            new TenantPaymentRepository(_db),
            new PaymentAuditLogWriter(_db),
            _db,
            new FixedCorrelationContext(),
            _adapters,
            new PrefixSecretProtector(),
            NullLogger<TenantPaymentConfig>.Instance,
            CancellationToken.None
        );

        Assert.True(deleted.IsSuccess);
        Assert.Equal(["we_1"], _stripe.Deleted);
        await using var fresh = InMemoryPaymentClient.CreateDb(_databaseName);
        Assert.Empty(await fresh.TenantPaymentConfigs.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task The_catalog_and_the_config_expose_the_setup()
    {
        await SeedConfigAsync();
        await SaveSecretsAsync("sk_live_1", webhookSecret: null);

        var catalog = ListAvailablePaymentProvidersHandler.Handle(
            new ListAvailablePaymentProvidersQuery(),
            _adapters,
            Options.Create(_options)
        );
        var off = ListAvailablePaymentProvidersHandler.Handle(
            new ListAvailablePaymentProvidersQuery(),
            _adapters,
            Options.Create(
                new PaymentWebhookOptions { PublicBaseUrl = "https://api.example.test", AutoRegister = false }
            )
        );
        var config = await GetTenantPaymentConfigHandler.Handle(
            new GetTenantPaymentConfigQuery(TenantId, PaymentProviderCode.Stripe),
            new TenantPaymentConfigRepository(_db),
            new TenantPaymentRepository(_db),
            CancellationToken.None
        );

        Assert.True(Assert.Single(catalog.Value).WebhookAutoSetup);
        Assert.False(Assert.Single(off.Value).WebhookAutoSetup);
        Assert.Equal("Pending", config.Value.WebhookSetup);
    }

    private Task<Result> SaveSecretsAsync(string secretKey, string? webhookSecret) =>
        UpdateTenantPaymentConfigSecretsHandler.Handle(
            new UpdateTenantPaymentConfigSecretsCommand(
                TenantId,
                PaymentProviderCode.Stripe,
                secretKey,
                webhookSecret,
                ActorId
            ),
            new TenantPaymentConfigRepository(_db),
            _adapters,
            new PrefixSecretProtector(),
            new PaymentAuditLogWriter(_db),
            _db,
            new FixedCorrelationContext(),
            Options.Create(_options),
            InMemoryPaymentClient.RecordingBus(_published),
            NullLogger<TenantPaymentConfig>.Instance,
            CancellationToken.None
        );

    private Task<Result> ActivateAsync() =>
        ActivateTenantPaymentConfigHandler.Handle(
            new ActivateTenantPaymentConfigCommand(TenantId, PaymentProviderCode.Stripe, ActorId),
            new TenantPaymentConfigRepository(_db),
            _adapters,
            new PaymentAuditLogWriter(_db),
            _db,
            new FixedCorrelationContext(),
            Options.Create(_options),
            InMemoryPaymentClient.RecordingBus(_published),
            CancellationToken.None
        );

    private Task RegisterAsync(int attempt = 1) =>
        RegisterTenantPaymentWebhookHandler.Handle(
            new RegisterTenantPaymentWebhookCommand(TenantId, PaymentProviderCode.Stripe, attempt),
            new TenantPaymentConfigRepository(_db),
            _adapters,
            new PrefixSecretProtector(),
            new FakeUrlBuilder(),
            Options.Create(_options),
            _db,
            InMemoryPaymentClient.RecordingBus(_published),
            NullLogger<TenantPaymentConfig>.Instance,
            CancellationToken.None
        );

    private async Task SeedConfigAsync()
    {
        _db.TenantPaymentConfigs.Add(
            TenantPaymentConfig
                .Create(
                    TenantId,
                    PaymentProviderCode.Stripe,
                    TenantPaymentMode.DirectApiKeys,
                    "pk_live_1",
                    StatementDescriptor.Create("ACME TAX SVC").Value,
                    DateTime.UtcNow
                )
                .Value
        );
        await _db.SaveChangesAsync();
    }

    private async Task<TenantPaymentConfig> ReloadAsync()
    {
        await using var fresh = InMemoryPaymentClient.CreateDb(_databaseName);
        return await fresh.TenantPaymentConfigs.IgnoreQueryFilters().SingleAsync();
    }

    private sealed class FakeUrlBuilder : IPaymentWebhookUrlBuilder
    {
        public Uri? Build(string webhookRouteName, Guid tenantId) =>
            new($"https://api.example.test/{webhookRouteName}/{tenantId}");
    }

    /// <summary>Proveedor falso con registro de webhooks: anota lo pedido y responde lo configurado.</summary>
    private sealed class RegisteringProvider : IPaymentProvider, IPaymentWebhookRegistration
    {
        public static readonly IReadOnlyList<string> Events = ["charge.refunded", "charge.dispute.created"];

        public List<(
            TenantProviderCredentials Credentials,
            WebhookRegistrationRequest Request
        )> Registrations { get; } = [];
        public List<string> Deleted { get; } = [];
        public Func<Result<RegisteredWebhook>> Next { get; set; } =
            () => Result.Success(new RegisteredWebhook("we_1", "whsec_auto"));
        public Result DeleteResult { get; set; } = Result.Success();

        public PaymentProviderCode Code => PaymentProviderCode.Stripe;

        public PaymentProviderDescriptor Descriptor { get; } =
            new(
                PaymentProviderCode.Stripe,
                "Stripe",
                "Publishable key",
                "Secret key",
                UsesWebhookSecret: true,
                AcceptsApiBaseUrl: false,
                WebhookEvents: Events,
                WebhookRouteName: PaymentWebhookRoutes.Stripe
            );

        public Task<Result<RegisteredWebhook>> RegisterWebhookAsync(
            TenantProviderCredentials credentials,
            WebhookRegistrationRequest request,
            CancellationToken ct
        )
        {
            Registrations.Add((credentials, request));
            return Task.FromResult(Next());
        }

        public Task<Result> DeleteWebhookAsync(
            TenantProviderCredentials credentials,
            string providerWebhookId,
            CancellationToken ct
        )
        {
            Deleted.Add(providerWebhookId);
            return Task.FromResult(DeleteResult);
        }

        public Task<Result<ChargeAuthorizationResult>> AuthorizeChargeAsync(
            TenantProviderCredentials credentials,
            ChargeAuthorizationRequest request,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<RefundResult>> RefundAsync(
            TenantProviderCredentials credentials,
            string providerChargeReference,
            Money amount,
            string reason,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<WebhookVerificationResult>> VerifyWebhookSignatureAsync(
            string rawPayload,
            IReadOnlyDictionary<string, string> headers,
            TenantProviderCredentials credentials,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<WebhookEventPayload>> ParseWebhookEventAsync(
            string rawPayload,
            string eventType,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<WebhookEventPayload>> GetChargeStatusAsync(
            TenantProviderCredentials credentials,
            ChargeStatusLookup lookup,
            CancellationToken ct
        ) => throw new NotSupportedException();
    }
}
