using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.ActivateTenantPaymentConfig;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.CreateTenantPaymentConfig;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.UpdateTenantPaymentConfigSecrets;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.UpdateTenantPaymentConfigUrl;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>Comandos de configuración contra el catálogo real: proveedor sin adaptador → 400, y el
/// webhook secret (en PayPal, el Webhook ID) es opcional: activa sin él y un PUT vacío no lo borra.</summary>
public sealed class TenantPaymentConfigCommandsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private readonly PaymentClientDbContext _db = InMemoryPaymentClient.CreateDb();
    private readonly IPaymentAdapterFactory _adapters = InMemoryPaymentClient.RealAdapters();

    // Registro automático apagado (sin base pública), como en local.
    private readonly PaymentWebhookOptions _webhooks = new();
    private readonly List<object> _published = [];

    [Fact]
    public async Task Creating_a_provider_without_adapter_is_a_400_not_supported()
    {
        var result = await CreateAsync(PaymentProviderCode.Square);

        AssertNotSupported(result);
        Assert.Empty(await _db.TenantPaymentConfigs.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Updating_secrets_url_or_activating_a_provider_without_adapter_is_a_400_not_supported()
    {
        var secrets = await UpdateSecretsAsync(PaymentProviderCode.Square, "secret", "whsec");
        var url = await UpdateTenantPaymentConfigUrlHandler.Handle(
            new UpdateTenantPaymentConfigUrlCommand(TenantId, PaymentProviderCode.Square, "https://x.test", ActorId),
            Configs(),
            _adapters,
            new PaymentAuditLogWriter(_db),
            _db,
            new FixedCorrelationContext(),
            CancellationToken.None
        );
        var activate = await ActivateAsync(PaymentProviderCode.Square);

        AssertNotSupported(secrets);
        AssertNotSupported(url);
        AssertNotSupported(activate);
    }

    [Fact]
    public async Task PayPal_saved_without_webhook_id_is_active_without_webhook_secret()
    {
        await CreateAsync(PaymentProviderCode.PayPal);

        var result = await UpdateSecretsAsync(PaymentProviderCode.PayPal, "paypal-client-secret", webhookSecret: null);

        Assert.True(result.IsSuccess);
        var config = await LoadAsync(PaymentProviderCode.PayPal);
        Assert.True(config.IsActive);
        Assert.NotNull(config.SecretKeyEncrypted);
        Assert.Null(config.WebhookSecretEncrypted);
    }

    [Fact]
    public async Task PayPal_saved_with_webhook_id_is_active()
    {
        await CreateAsync(PaymentProviderCode.PayPal);

        var result = await UpdateSecretsAsync(PaymentProviderCode.PayPal, "paypal-client-secret", "WH-ID-123");

        Assert.True(result.IsSuccess);
        var config = await LoadAsync(PaymentProviderCode.PayPal);
        Assert.True(config.IsActive);
        Assert.NotNull(config.SecretKeyEncrypted);
        Assert.NotNull(config.WebhookSecretEncrypted);
    }

    [Fact]
    public async Task Stripe_saved_with_a_blank_webhook_secret_is_active_without_webhook_secret()
    {
        await CreateAsync(PaymentProviderCode.Stripe);

        var result = await UpdateSecretsAsync(PaymentProviderCode.Stripe, "sk_test_x", webhookSecret: "  ");

        Assert.True(result.IsSuccess);
        var config = await LoadAsync(PaymentProviderCode.Stripe);
        Assert.True(config.IsActive);
        Assert.Null(config.WebhookSecretEncrypted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Saving_secrets_with_an_empty_webhook_secret_keeps_the_stored_one(string? webhookSecret)
    {
        await CreateAsync(PaymentProviderCode.Stripe);
        await UpdateSecretsAsync(PaymentProviderCode.Stripe, "sk_test_old", "whsec_saved");

        var result = await UpdateSecretsAsync(PaymentProviderCode.Stripe, "sk_test_new", webhookSecret);

        Assert.True(result.IsSuccess);
        var config = await LoadAsync(PaymentProviderCode.Stripe);
        Assert.Equal("enc:whsec_saved", config.WebhookSecretEncrypted?.CipherText);
        Assert.Equal("enc:sk_test_new", config.SecretKeyEncrypted?.CipherText);
        Assert.True(config.IsActive);
    }

    [Fact]
    public async Task Saving_secrets_with_a_new_webhook_secret_replaces_the_stored_one()
    {
        await CreateAsync(PaymentProviderCode.PayPal);
        await UpdateSecretsAsync(PaymentProviderCode.PayPal, "paypal-client-secret", "WH-OLD");

        await UpdateSecretsAsync(PaymentProviderCode.PayPal, "paypal-client-secret", "WH-NEW");

        Assert.Equal("enc:WH-NEW", (await LoadAsync(PaymentProviderCode.PayPal)).WebhookSecretEncrypted?.CipherText);
    }

    [Fact]
    public async Task Stripe_saved_with_webhook_secret_is_active()
    {
        await CreateAsync(PaymentProviderCode.Stripe);

        var result = await UpdateSecretsAsync(PaymentProviderCode.Stripe, "sk_test_x", "whsec_x");

        Assert.True(result.IsSuccess);
        Assert.True((await LoadAsync(PaymentProviderCode.Stripe)).IsActive);
    }

    [Fact]
    public async Task Activating_PayPal_again_after_turning_it_off_keeps_its_webhook_id()
    {
        await CreateAsync(PaymentProviderCode.PayPal);
        await UpdateSecretsAsync(PaymentProviderCode.PayPal, "paypal-client-secret", "WH-ID-123");
        (await LoadAsync(PaymentProviderCode.PayPal)).Deactivate("paused", ActorId, DateTime.UtcNow);
        await _db.SaveChangesAsync();

        var result = await ActivateAsync(PaymentProviderCode.PayPal);

        Assert.True(result.IsSuccess);
        Assert.True((await LoadAsync(PaymentProviderCode.PayPal)).IsActive);
    }

    private static void AssertNotSupported(Result result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(PaymentProviderSupport.NotSupportedCode, result.Error.Code);
        Assert.Equal(400, result.Error.ToHttpStatusCode());
    }

    private TenantPaymentConfigRepository Configs() => new(_db);

    private Task<Result<Guid>> CreateAsync(PaymentProviderCode provider) =>
        CreateTenantPaymentConfigHandler.Handle(
            new CreateTenantPaymentConfigCommand(
                TenantId,
                provider,
                TenantPaymentMode.DirectApiKeys,
                "public-key",
                "ACME TAX SVC",
                ActorId
            ),
            Configs(),
            _adapters,
            new PaymentAuditLogWriter(_db),
            _db,
            new FixedCorrelationContext(),
            CancellationToken.None
        );

    private Task<Result> UpdateSecretsAsync(PaymentProviderCode provider, string secretKey, string? webhookSecret) =>
        UpdateTenantPaymentConfigSecretsHandler.Handle(
            new UpdateTenantPaymentConfigSecretsCommand(TenantId, provider, secretKey, webhookSecret, ActorId),
            Configs(),
            _adapters,
            new PrefixSecretProtector(),
            new PaymentAuditLogWriter(_db),
            _db,
            new FixedCorrelationContext(),
            Options.Create(_webhooks),
            InMemoryPaymentClient.RecordingBus(_published),
            NullLogger<TenantPaymentConfig>.Instance,
            CancellationToken.None
        );

    private Task<Result> ActivateAsync(PaymentProviderCode provider) =>
        ActivateTenantPaymentConfigHandler.Handle(
            new ActivateTenantPaymentConfigCommand(TenantId, provider, ActorId),
            Configs(),
            _adapters,
            new PaymentAuditLogWriter(_db),
            _db,
            new FixedCorrelationContext(),
            Options.Create(_webhooks),
            InMemoryPaymentClient.RecordingBus(_published),
            CancellationToken.None
        );

    private async Task<TenantPaymentConfig> LoadAsync(PaymentProviderCode provider) =>
        (await Configs().GetByTenantAndProviderAsync(TenantId, provider))!;
}
