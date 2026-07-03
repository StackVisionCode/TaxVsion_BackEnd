using BuildingBlocks.Domain;

namespace TaxVision.Payment.Domain.StripeCustomers;

/// <summary>
/// Represents the mapping between a TaxVision tenant and its corresponding Stripe customer object.
/// <para>
/// Created on demand when a tenant makes its first SaaS payment. Cached to avoid repeated
/// Stripe customer-search API calls on every billing event.
/// </para>
/// </summary>
public sealed class StripeCustomer : TenantEntity
{
    /// <summary>Stripe's customer ID (format: <c>cus_...</c>).</summary>
    public string StripeCustomerId { get; private set; } = string.Empty;

    /// <summary>Email used when creating the Stripe customer record (typically the tenant admin email).</summary>
    public string AdminEmail { get; private set; } = string.Empty;

    /// <summary>UTC timestamp when this mapping was first created.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    private StripeCustomer() { }

    /// <summary>
    /// Creates a new <see cref="StripeCustomer"/> mapping for a tenant.
    /// </summary>
    /// <param name="tenantId">TaxVision tenant this Stripe customer belongs to.</param>
    /// <param name="stripeCustomerId">Stripe customer ID returned by the Stripe API.</param>
    /// <param name="adminEmail">Email address used when registering the customer in Stripe.</param>
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
