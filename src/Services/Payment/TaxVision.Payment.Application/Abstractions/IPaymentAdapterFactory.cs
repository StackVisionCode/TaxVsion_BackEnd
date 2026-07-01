using TaxVision.Payment.Domain.TenantPayments;

namespace TaxVision.Payment.Application.Abstractions;

public interface IPaymentAdapterFactory
{
    IPaymentAdapter GetAdapter(TenantPaymentProvider provider);
}
