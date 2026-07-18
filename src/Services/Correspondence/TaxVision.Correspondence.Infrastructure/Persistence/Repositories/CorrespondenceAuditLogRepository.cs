using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Audit;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

public sealed class CorrespondenceAuditLogRepository(CorrespondenceDbContext db) : ICorrespondenceAuditLogRepository
{
    public async Task AddAsync(CorrespondenceAuditLog entity, CancellationToken ct = default)
    {
        await db.CorrespondenceAuditLogs.AddAsync(entity, ct);
    }
}
