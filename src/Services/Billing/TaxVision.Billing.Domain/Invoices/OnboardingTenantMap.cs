namespace TaxVision.Billing.Domain.Invoices;

/// <summary>BIL-5: qué tenant salió de cada onboarding. Se guarda exista o no la factura, así el re-hospedaje no
/// depende del orden en que lleguen el alta del tenant y el pedido de la factura.</summary>
public sealed class OnboardingTenantMap
{
    public Guid OnboardingId { get; private set; }
    public Guid TenantId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private OnboardingTenantMap() { }

    public static OnboardingTenantMap Create(Guid onboardingId, Guid tenantId, DateTime nowUtc) =>
        new()
        {
            OnboardingId = onboardingId,
            TenantId = tenantId,
            CreatedAtUtc = nowUtc,
        };

    public void PointTo(Guid tenantId) => TenantId = tenantId;
}
