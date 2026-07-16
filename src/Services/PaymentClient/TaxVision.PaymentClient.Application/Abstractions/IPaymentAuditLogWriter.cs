using TaxVision.PaymentClient.Domain.Audit;

namespace TaxVision.PaymentClient.Application.Abstractions;

public interface IPaymentAuditLogWriter
{
    Task AppendAsync(PaymentAuditEntry entry, CancellationToken ct = default);
}
