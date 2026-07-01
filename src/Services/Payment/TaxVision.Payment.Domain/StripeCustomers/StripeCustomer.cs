using BuildingBlocks.Domain;

namespace TaxVision.Payment.Domain.StripeCustomers;

public sealed class StripeCustomer : TenantEntity
{
    public string StripeCustomerId { get; private set; } = string.Empty;
    public string AdminEmail { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    private StripeCustomer() { }

    public static StripeCustomer Create(Guid tenantId, string stripeCustomerId, string adminEmail)
    {
        var customer = new StripeCustomer
        {
            StripeCustomerId = stripeCustomerId,
            AdminEmail = adminEmail,
            CreatedAtUtc = DateTime.UtcNow
        };
        customer.SetTenant(tenantId);
        return customer;
    }
}
