using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Tests.Domain;

public sealed class TenantPaymentConfigTests
{
    [Fact]
    public void Create_with_an_empty_tenant_fails()
    {
        var result = TenantPaymentConfig.Create(
            Guid.Empty,
            PaymentProviderCode.Stripe,
            TenantPaymentMode.DirectApiKeys,
            "pk_test_123",
            Descriptor(),
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantPaymentConfig.InvalidTenant", result.Error.Code);
    }

    [Fact]
    public void Create_starts_inactive_with_no_secrets_loaded()
    {
        var config = CreateConfig();

        Assert.False(config.IsActive);
        Assert.Null(config.SecretKeyEncrypted);
        Assert.Null(config.WebhookSecretEncrypted);
        Assert.Null(config.SettledAtUtc);
    }

    [Fact]
    public void MarkActive_with_no_secrets_loaded_is_rejected()
    {
        var config = CreateConfig();

        var result = config.MarkActive(Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantPaymentConfig.SecretsMissing", result.Error.Code);
        Assert.False(config.IsActive);
    }

    [Fact]
    public void MarkActive_after_UpdateSecrets_succeeds_and_stamps_SettledAtUtc()
    {
        var config = CreateConfig();
        var nowUtc = DateTime.UtcNow;
        config.UpdateSecrets(Secret("sk_live_x"), Secret("whsec_x"), Guid.Empty, nowUtc);

        var result = config.MarkActive(Guid.Empty, nowUtc);

        Assert.True(result.IsSuccess);
        Assert.True(config.IsActive);
        Assert.Equal(nowUtc, config.SettledAtUtc);
    }

    [Fact]
    public void MarkActive_a_second_time_does_not_move_SettledAtUtc()
    {
        var config = CreateConfig();
        var firstActivation = DateTime.UtcNow;
        config.UpdateSecrets(Secret("sk_live_x"), Secret("whsec_x"), Guid.Empty, firstActivation);
        config.MarkActive(Guid.Empty, firstActivation);

        var laterActivation = firstActivation.AddDays(1);
        config.RotateWebhookSecret(Secret("whsec_y"), Guid.Empty, laterActivation);
        config.MarkActive(Guid.Empty, laterActivation);

        Assert.Equal(firstActivation, config.SettledAtUtc);
    }

    [Fact]
    public void Deactivate_with_no_reason_fails()
    {
        var config = CreateConfig();

        var result = config.Deactivate("  ", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantPaymentConfig.InvalidReason", result.Error.Code);
    }

    [Fact]
    public void Deactivate_turns_an_active_config_off()
    {
        var config = CreateConfig();
        var nowUtc = DateTime.UtcNow;
        config.UpdateSecrets(Secret("sk_live_x"), Secret("whsec_x"), Guid.Empty, nowUtc);
        config.MarkActive(Guid.Empty, nowUtc);

        var result = config.Deactivate("compromised key", Guid.Empty, nowUtc);

        Assert.True(result.IsSuccess);
        Assert.False(config.IsActive);
    }

    [Fact]
    public void RotateWebhookSecret_replaces_only_the_webhook_secret()
    {
        var config = CreateConfig();
        var nowUtc = DateTime.UtcNow;
        config.UpdateSecrets(Secret("sk_live_x"), Secret("whsec_x"), Guid.Empty, nowUtc);

        config.RotateWebhookSecret(Secret("whsec_y"), Guid.Empty, nowUtc);

        Assert.Equal("sk_live_x", config.SecretKeyEncrypted!.CipherText);
        Assert.Equal("whsec_y", config.WebhookSecretEncrypted!.CipherText);
    }

    private static EncryptedSecret Secret(string cipherText) => EncryptedSecret.Create(cipherText).Value;

    [Fact]
    public void Activates_with_only_the_secret_key_because_the_webhook_secret_is_optional()
    {
        var config = CreateConfig(PaymentProviderCode.PayPal);
        var nowUtc = DateTime.UtcNow;

        var update = config.UpdateSecrets(Secret("paypal_secret"), null, Guid.Empty, nowUtc);
        var activate = config.MarkActive(Guid.Empty, nowUtc);

        Assert.True(update.IsSuccess);
        Assert.True(activate.IsSuccess);
        Assert.True(config.IsActive);
        Assert.Null(config.WebhookSecretEncrypted);
    }

    [Fact]
    public void UpdateSecrets_without_a_webhook_secret_keeps_the_stored_one()
    {
        var config = CreateConfig();
        var nowUtc = DateTime.UtcNow;
        config.UpdateSecrets(Secret("sk_live_x"), Secret("whsec_x"), Guid.Empty, nowUtc);

        var result = config.UpdateSecrets(Secret("sk_live_y"), null, Guid.Empty, nowUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal("sk_live_y", config.SecretKeyEncrypted!.CipherText);
        Assert.Equal("whsec_x", config.WebhookSecretEncrypted!.CipherText);
    }

    [Fact]
    public void UpdateSecrets_on_a_Connect_mode_config_is_rejected()
    {
        var config = CreateConnectConfig();

        var result = config.UpdateSecrets(Secret("sk_live_x"), Secret("whsec_x"), Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantPaymentConfig.WrongMode", result.Error.Code);
    }

    [Fact]
    public void MarkActive_on_a_Connect_mode_config_is_rejected()
    {
        var config = CreateConnectConfig();

        var result = config.MarkActive(Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantPaymentConfig.WrongMode", result.Error.Code);
    }

    [Fact]
    public void MarkActiveViaConnect_on_a_DirectApiKeys_config_is_rejected()
    {
        var config = CreateConfig();

        var result = config.MarkActiveViaConnect(Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantPaymentConfig.WrongMode", result.Error.Code);
    }

    [Fact]
    public void MarkActiveViaConnect_on_a_Connect_mode_config_activates_it_with_no_secrets_needed()
    {
        var config = CreateConnectConfig();
        var nowUtc = DateTime.UtcNow;

        var result = config.MarkActiveViaConnect(Guid.Empty, nowUtc);

        Assert.True(result.IsSuccess);
        Assert.True(config.IsActive);
        Assert.Equal(nowUtc, config.SettledAtUtc);
        Assert.Null(config.SecretKeyEncrypted);
    }

    private static StatementDescriptor Descriptor() => StatementDescriptor.Create("ACME TAX SVC").Value;

    private static TenantPaymentConfig CreateConfig(PaymentProviderCode provider = PaymentProviderCode.Stripe) =>
        TenantPaymentConfig
            .Create(
                Guid.NewGuid(),
                provider,
                TenantPaymentMode.DirectApiKeys,
                "pk_test_123",
                Descriptor(),
                DateTime.UtcNow
            )
            .Value;

    private static TenantPaymentConfig CreateConnectConfig() =>
        TenantPaymentConfig
            .Create(
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                TenantPaymentMode.Connect,
                "pk_test_123",
                Descriptor(),
                DateTime.UtcNow
            )
            .Value;
}
