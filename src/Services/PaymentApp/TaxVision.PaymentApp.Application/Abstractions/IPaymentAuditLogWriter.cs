using TaxVision.PaymentApp.Domain.Audit;

namespace TaxVision.PaymentApp.Application.Abstractions;

public interface IPaymentAuditLogWriter
{
    Task AppendAsync(PaymentAuditEntry entry, CancellationToken ct = default);
}
