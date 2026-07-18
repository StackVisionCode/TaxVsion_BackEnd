using TaxVision.PaymentClient.Domain.Connect;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.Abstractions;

public interface ITenantConnectAccountRepository
{
    Task<TenantConnectAccount?> GetByTenantAndProviderAsync(
        Guid tenantId,
        PaymentProviderCode code,
        CancellationToken ct = default
    );

    /// <summary>Lookup por el id de Stripe — usado por el webhook de Connect, que llega sin
    /// tenant en el path (el <c>account</c> del payload es lo único que identifica al
    /// tenant).</summary>
    Task<TenantConnectAccount?> GetByStripeConnectAccountIdAsync(
        string stripeConnectAccountId,
        CancellationToken ct = default
    );

    Task AddAsync(TenantConnectAccount account, CancellationToken ct = default);
}
