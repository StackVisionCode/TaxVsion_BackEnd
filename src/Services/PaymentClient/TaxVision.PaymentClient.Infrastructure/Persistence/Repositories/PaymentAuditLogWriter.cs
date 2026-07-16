using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.Audit;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

public sealed class PaymentAuditLogWriter(PaymentClientDbContext db) : IPaymentAuditLogWriter
{
    public async Task AppendAsync(PaymentAuditEntry entry, CancellationToken ct = default) =>
        await db.AuditEntries.AddAsync(entry, ct);
}
