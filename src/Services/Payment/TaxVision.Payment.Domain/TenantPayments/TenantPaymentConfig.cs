using BuildingBlocks.Domain;

namespace TaxVision.Payment.Domain.TenantPayments;

/// <summary>
/// Stores the payment provider configuration that a tenant uses to charge its own customers
/// (i.e. the tenant-side payment context, separate from SaaS platform billing).
/// <para>
/// Secret keys are stored AES-encrypted; they are NEVER exposed in HTTP responses.
/// The <see cref="IPaymentAdapterFactory"/> decrypts them at runtime before delegating
/// to the appropriate <see cref="IPaymentAdapter"/>.
/// </para>
/// </summary>
public sealed class TenantPaymentConfig : TenantEntity
{
    /// <summary>Payment provider selected by the tenant (Stripe, PayPal, Square, etc.).</summary>
    public TenantPaymentProvider Provider { get; private set; }

    /// <summary>Whether this configuration is currently active and usable for charges.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Provider public key (safe to store in plain text; used by the frontend).</summary>
    public string? PublicKey { get; private set; }

    /// <summary>AES-encrypted provider secret key. Never exposed in API responses.</summary>
    public string? SecretKeyEncrypted { get; private set; }

    /// <summary>AES-encrypted provider webhook secret. Never exposed in API responses.</summary>
    public string? WebhookSecretEncrypted { get; private set; }

    /// <summary>UTC timestamp when this configuration was first created.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>UTC timestamp of the last configuration update.</summary>
    public DateTime UpdatedAtUtc { get; private set; }

    private TenantPaymentConfig() { }

    /// <summary>Creates a new active payment provider configuration for a tenant.</summary>
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

    /// <summary>Updates the provider credentials and reactivates the configuration.</summary>
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

    /// <summary>Deactivates this configuration. Charges will fail until re-configured.</summary>
    public void Deactivate()
    {
        IsActive = false;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
