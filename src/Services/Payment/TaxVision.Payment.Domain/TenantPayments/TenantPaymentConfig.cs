using BuildingBlocks.Domain;

namespace TaxVision.Payment.Domain.TenantPayments;

public sealed class TenantPaymentConfig : TenantEntity
{
    public TenantPaymentProvider Provider { get; private set; }
    public bool IsActive { get; private set; }
    public string? PublicKey { get; private set; }
    public string? SecretKeyEncrypted { get; private set; }
    public string? WebhookSecretEncrypted { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private TenantPaymentConfig() { }

    public static TenantPaymentConfig Create(
        Guid tenantId,
        TenantPaymentProvider provider,
        string? publicKey,
        string? secretKeyEncrypted,
        string? webhookSecretEncrypted)
    {
        var config = new TenantPaymentConfig
        {
            Provider = provider,
            IsActive = true,
            PublicKey = publicKey,
            SecretKeyEncrypted = secretKeyEncrypted,
            WebhookSecretEncrypted = webhookSecretEncrypted,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        config.SetTenant(tenantId);
        return config;
    }

    public void Configure(
        TenantPaymentProvider provider,
        string? publicKey,
        string? secretKeyEncrypted,
        string? webhookSecretEncrypted)
    {
        Provider = provider;
        PublicKey = publicKey;
        SecretKeyEncrypted = secretKeyEncrypted;
        WebhookSecretEncrypted = webhookSecretEncrypted;
        IsActive = true;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
