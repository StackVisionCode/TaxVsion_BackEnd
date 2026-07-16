using BuildingBlocks.Domain;

namespace TaxVision.PaymentApp.Domain.ProviderCustomers;

/// <summary>
/// Un método de pago tokenizado guardado por el tenant en el provider (tarjeta, cuenta
/// bancaria). Entidad hija de <see cref="TenantProviderCustomer"/>: su configuración EF
/// requiere <c>ValueGeneratedNever()</c> (guardrail §49). <see cref="MethodReference"/> es
/// opaco — el dominio nunca interpreta su formato, solo lo reenvía al adapter.
/// </summary>
public sealed class TenantSavedPaymentMethod : BaseEntity
{
    public Guid TenantProviderCustomerId { get; private set; }
    public Guid TenantId { get; private set; }
    public string MethodReference { get; private set; } = default!;
    public string Brand { get; private set; } = default!;
    public string Last4 { get; private set; } = default!;
    public int ExpMonth { get; private set; }
    public int ExpYear { get; private set; }
    public bool IsDefault { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public bool IsDetached { get; private set; }
    public DateTime? DetachedAtUtc { get; private set; }
    public DateTime? ExpiryNoticeSentAtUtc { get; private set; }

    private TenantSavedPaymentMethod() { }

    public static TenantSavedPaymentMethod Create(
        Guid tenantProviderCustomerId,
        Guid tenantId,
        string methodReference,
        string brand,
        string last4,
        int expMonth,
        int expYear,
        bool isDefault,
        DateTime nowUtc) =>
        new()
        {
            TenantProviderCustomerId = tenantProviderCustomerId,
            TenantId = tenantId,
            MethodReference = methodReference,
            Brand = brand,
            Last4 = last4,
            ExpMonth = expMonth,
            ExpYear = expYear,
            IsDefault = isDefault,
            CreatedAtUtc = nowUtc,
        };

    /// <summary>Vencido si su período expiró antes del inicio del mes indicado — usado por
    /// <c>ExpiringPaymentMethodsJob</c> para avisar antes de que ocurra.</summary>
    public bool ExpiresBefore(DateTime cutoffUtc) => new DateTime(ExpYear, ExpMonth, 1).AddMonths(1) <= cutoffUtc;

    public void SetDefault(bool isDefault) => IsDefault = isDefault;

    public void MarkDetached(DateTime nowUtc)
    {
        IsDetached = true;
        DetachedAtUtc = nowUtc;
        IsDefault = false;
    }

    /// <summary>Un solo aviso por método — <c>ExpiringPaymentMethodsJob</c> no vuelve a
    /// notificar el mismo vencimiento en cada corrida.</summary>
    public void MarkExpiryNoticeSent(DateTime nowUtc) => ExpiryNoticeSentAtUtc = nowUtc;
}
