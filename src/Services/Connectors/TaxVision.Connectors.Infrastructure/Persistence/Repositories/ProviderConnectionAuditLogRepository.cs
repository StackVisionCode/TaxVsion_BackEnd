using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Application.Audit;
using TaxVision.Connectors.Domain.Audit;

namespace TaxVision.Connectors.Infrastructure.Persistence.Repositories;

public sealed class ProviderConnectionAuditLogRepository(ConnectorsDbContext dbContext)
    : IProviderConnectionAuditLogRepository
{
    public async Task AddAsync(ProviderConnectionAuditLog entry, CancellationToken ct = default) =>
        await dbContext.ProviderConnectionAuditLogs.AddAsync(entry, ct);

    public async Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default) =>
        await dbContext
            .ProviderConnectionAuditLogs.Where(e => e.Timestamp < cutoffUtc)
            .Take(batchSize)
            .ExecuteDeleteAsync(ct);
}
