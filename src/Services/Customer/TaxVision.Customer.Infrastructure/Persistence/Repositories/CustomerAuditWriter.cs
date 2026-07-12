using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Audit;

namespace TaxVision.Customer.Infrastructure.Persistence.Repositories;

public sealed class CustomerAuditWriter(CustomerDbContext db) : ICustomerAuditWriter
{
    public Task AddAsync(CustomerAuditLog log, CancellationToken ct)
    {
        db.CustomerAuditLogs.Add(log);
        return Task.CompletedTask;
    }
}
