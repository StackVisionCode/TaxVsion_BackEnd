using BuildingBlocks.Domain;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Domain.TenantPaymentConfigs;

/// <summary>
/// URL propia del tenant a la que PaymentClient reenvía (relay) los eventos de un cobro
/// procesado — el tenant puede tener su propio sistema externo que necesita enterarse en
/// tiempo real. Entidad hija de <see cref="TenantPaymentConfig"/>: requiere
/// <c>ValueGeneratedNever()</c>. La llamada de relay en sí (HTTP saliente firmado con
/// <see cref="SigningSecret"/>) es responsabilidad de Infrastructure, no de este dominio.
/// </summary>
public sealed class TenantWebhookEndpoint : BaseEntity
{
    public Guid TenantPaymentConfigId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Url { get; private set; } = default!;
    public EncryptedSecret SigningSecret { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private TenantWebhookEndpoint() { }

    public static TenantWebhookEndpoint Create(
        Guid tenantPaymentConfigId,
        Guid tenantId,
        string url,
        EncryptedSecret signingSecret,
        DateTime nowUtc
    ) =>
        new()
        {
            TenantPaymentConfigId = tenantPaymentConfigId,
            TenantId = tenantId,
            Url = url,
            SigningSecret = signingSecret,
            IsActive = true,
            CreatedAtUtc = nowUtc,
        };

    public void Deactivate() => IsActive = false;
}
