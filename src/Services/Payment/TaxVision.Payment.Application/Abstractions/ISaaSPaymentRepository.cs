using TaxVision.Payment.Domain.SaaSPayments;

namespace TaxVision.Payment.Application.Abstractions;

/// <summary>
/// Repository for SaaS platform payments processed by TaxVision against tenants.
/// Each record represents a Stripe PaymentIntent created on behalf of the platform.
/// </summary>
public interface ISaaSPaymentRepository
{
    /// <summary>Returns a SaaS payment by its primary key, or <c>null</c> if not found.</summary>
    Task<SaaSPayment?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Returns the SaaS payment associated with a given business reference (e.g. enrollment ID,
    /// seat subscription ID) and payment type, or <c>null</c> if not found.
    /// </summary>
    Task<SaaSPayment?> GetByReferenceIdAsync(Guid referenceId, SaaSPaymentType type, CancellationToken ct = default);

    /// <summary>
    /// Returns the SaaS payment whose <c>StripePaymentIntentId</c> matches <paramref name="paymentIntentId"/>,
    /// or <c>null</c> if not found. Used by the Stripe webhook handler to locate the payment after
    /// a <c>payment_intent.succeeded</c> or <c>payment_intent.payment_failed</c> event.
    /// </summary>
    Task<SaaSPayment?> GetByStripePaymentIntentIdAsync(string paymentIntentId, CancellationToken ct = default);

    /// <summary>Adds a new SaaS payment to the unit of work (persisted on SaveChanges).</summary>
    Task AddAsync(SaaSPayment payment, CancellationToken ct = default);

    /// <summary>Returns all SaaS payments for the given tenant, ordered newest-first.</summary>
    Task<List<SaaSPayment>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
}
