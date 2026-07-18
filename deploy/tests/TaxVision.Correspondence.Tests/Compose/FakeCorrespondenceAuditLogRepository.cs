using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Audit;

namespace TaxVision.Correspondence.Tests.Compose;

internal sealed class FakeCorrespondenceAuditLogRepository : ICorrespondenceAuditLogRepository
{
    private readonly List<CorrespondenceAuditLog> _store = [];

    public IReadOnlyList<CorrespondenceAuditLog> All => _store;

    public Task AddAsync(CorrespondenceAuditLog entity, CancellationToken ct = default)
    {
        _store.Add(entity);
        return Task.CompletedTask;
    }
}
