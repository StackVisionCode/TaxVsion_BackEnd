using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Tests.Domain;

/// <summary>Estado del webhook en el config: el registro automático nunca pisa un secreto manual.</summary>
public sealed class TenantPaymentConfigWebhookSetupTests
{
    private static readonly DateTime NowUtc = DateTime.UtcNow;

    [Fact]
    public void A_new_config_has_no_webhook_setup()
    {
        var config = Direct();

        Assert.Null(config.WebhookSetup);
        Assert.Null(config.ProviderWebhookId);
    }

    [Fact]
    public void A_pasted_webhook_secret_is_Manual()
    {
        var config = Direct();

        config.UpdateSecrets(Secret("sk"), Secret("whsec_manual"), Guid.Empty, NowUtc);

        Assert.Equal(WebhookSetupStatus.Manual, config.WebhookSetup);
    }

    [Fact]
    public void Saving_keys_without_a_webhook_secret_keeps_the_setup_state()
    {
        var config = Direct();
        config.UpdateSecrets(Secret("sk"), Secret("whsec_manual"), Guid.Empty, NowUtc);

        config.UpdateSecrets(Secret("sk2"), null, Guid.Empty, NowUtc);

        Assert.Equal(WebhookSetupStatus.Manual, config.WebhookSetup);
    }

    [Fact]
    public void The_automatic_setup_goes_pending_then_automatic_with_the_provider_id()
    {
        var config = Direct();
        config.UpdateSecrets(Secret("sk"), null, Guid.Empty, NowUtc);

        Assert.True(config.BeginAutomaticWebhookSetup(NowUtc).IsSuccess);
        Assert.Equal(WebhookSetupStatus.Pending, config.WebhookSetup);

        Assert.True(config.CompleteAutomaticWebhookSetup(Secret("whsec_auto"), "we_1", NowUtc).IsSuccess);
        Assert.Equal(WebhookSetupStatus.Automatic, config.WebhookSetup);
        Assert.Equal("we_1", config.ProviderWebhookId);
        Assert.Equal("whsec_auto", config.WebhookSecretEncrypted!.CipherText);
    }

    [Fact]
    public void Queuing_stamps_the_request_and_only_a_pending_setup_can_renew_it()
    {
        var config = Direct();
        config.UpdateSecrets(Secret("sk"), null, Guid.Empty, NowUtc);
        Assert.True(config.RenewWebhookSetupRequest(NowUtc).IsFailure);

        config.BeginAutomaticWebhookSetup(NowUtc);
        Assert.Equal(NowUtc, config.WebhookSetupRequestedAtUtc);

        var later = NowUtc.AddMinutes(30);
        Assert.True(config.RenewWebhookSetupRequest(later).IsSuccess);
        Assert.Equal(later, config.WebhookSetupRequestedAtUtc);
    }

    [Fact]
    public void A_manual_secret_that_arrives_while_pending_wins()
    {
        var config = Direct();
        config.UpdateSecrets(Secret("sk"), null, Guid.Empty, NowUtc);
        config.BeginAutomaticWebhookSetup(NowUtc);

        config.UpdateSecrets(Secret("sk"), Secret("whsec_manual"), Guid.Empty, NowUtc);
        var completed = config.CompleteAutomaticWebhookSetup(Secret("whsec_auto"), "we_1", NowUtc);

        Assert.True(completed.IsFailure);
        Assert.Equal(WebhookSetupStatus.Manual, config.WebhookSetup);
        Assert.Equal("whsec_manual", config.WebhookSecretEncrypted!.CipherText);
        Assert.Null(config.ProviderWebhookId);
    }

    [Fact]
    public void The_automatic_setup_never_starts_over_a_manual_secret_or_in_Connect_mode()
    {
        var manual = Direct();
        manual.UpdateSecrets(Secret("sk"), Secret("whsec_manual"), Guid.Empty, NowUtc);
        var connect = TenantPaymentConfig
            .Create(
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                TenantPaymentMode.Connect,
                "pk",
                StatementDescriptor.Create("ACME TAX SVC").Value,
                NowUtc
            )
            .Value;

        Assert.Equal("TenantPaymentConfig.ManualWebhook", manual.BeginAutomaticWebhookSetup(NowUtc).Error.Code);
        Assert.Equal("TenantPaymentConfig.WrongMode", connect.BeginAutomaticWebhookSetup(NowUtc).Error.Code);
    }

    [Fact]
    public void A_failed_setup_only_comes_from_pending()
    {
        var config = Direct();
        Assert.True(config.FailAutomaticWebhookSetup(NowUtc).IsFailure);

        config.UpdateSecrets(Secret("sk"), null, Guid.Empty, NowUtc);
        config.BeginAutomaticWebhookSetup(NowUtc);

        Assert.True(config.FailAutomaticWebhookSetup(NowUtc).IsSuccess);
        Assert.Equal(WebhookSetupStatus.Failed, config.WebhookSetup);
    }

    [Fact]
    public void ForgetProviderWebhook_only_clears_the_matching_id()
    {
        var config = Direct();
        config.UpdateSecrets(Secret("sk"), null, Guid.Empty, NowUtc);
        config.BeginAutomaticWebhookSetup(NowUtc);
        config.CompleteAutomaticWebhookSetup(Secret("whsec_auto"), "we_1", NowUtc);

        config.ForgetProviderWebhook("we_other");
        Assert.Equal("we_1", config.ProviderWebhookId);

        config.ForgetProviderWebhook("we_1");
        Assert.Null(config.ProviderWebhookId);
    }

    private static TenantPaymentConfig Direct() =>
        TenantPaymentConfig
            .Create(
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                TenantPaymentMode.DirectApiKeys,
                "pk",
                StatementDescriptor.Create("ACME TAX SVC").Value,
                NowUtc
            )
            .Value;

    private static EncryptedSecret Secret(string cipherText) => EncryptedSecret.Create(cipherText).Value;
}
