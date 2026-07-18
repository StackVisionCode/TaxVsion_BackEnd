using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.Audit;

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Repositories;

public sealed class PaymentAuditLogWriter(PaymentAppDbContext db) : IPaymentAuditLogWriter
{
    public async Task AppendAsync(PaymentAuditEntry entry, CancellationToken ct = default) =>
        await db.AuditEntries.AddAsync(entry, ct);
}
