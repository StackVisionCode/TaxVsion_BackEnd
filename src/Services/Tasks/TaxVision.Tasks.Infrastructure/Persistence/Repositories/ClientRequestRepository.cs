using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.ClientRequests.Abstractions;
using TaxVision.Tasks.Domain.ClientRequests;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

/// <summary>
/// Todas las lecturas llevan <c>IgnoreQueryFilters()</c> y el tenant explícito: el filtro global es
/// fail-closed y en el scope de un consumer devolvería 0 filas sin fallar.
/// </summary>
internal sealed class ClientRequestRepository(TasksDbContext context) : IClientRequestRepository
{
    public void Add(ClientRequest request) => context.ClientRequests.Add(request);

    public async Task<Result<ClientRequest>> GetByIdAsync(Guid tenantId, Guid requestId, CancellationToken ct = default)
    {
        var request = await context
            .ClientRequests.IgnoreQueryFilters()
            .Include(r => r.Documents)
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == requestId, ct);

        return request is null ? Result.Failure<ClientRequest>(ClientRequestErrors.NotFound) : Result.Success(request);
    }

    public async Task<IReadOnlyList<ClientRequest>> ListForCustomerAsync(
        Guid tenantId,
        Guid customerId,
        bool onlyOpen,
        CancellationToken ct = default
    ) =>
        await context
            .ClientRequests.IgnoreQueryFilters()
            .Include(r => r.Documents)
            .Where(r => r.TenantId == tenantId && r.CustomerId == customerId)
            .Where(r =>
                !onlyOpen || r.Status == ClientRequestStatus.Pending || r.Status == ClientRequestStatus.Submitted
            )
            .OrderBy(r => r.Status)
            .ThenBy(r => r.DueAtUtc ?? DateTime.MaxValue)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ClientRequest>> ListForTaskAsync(
        Guid tenantId,
        Guid taskId,
        CancellationToken ct = default
    ) =>
        await context
            .ClientRequests.IgnoreQueryFilters()
            .Include(r => r.Documents)
            .Where(r => r.TenantId == tenantId && r.TaskId == taskId)
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<ClientRequest?> GetByDocumentFileIdAsync(Guid fileId, CancellationToken ct = default) =>
        await context
            .ClientRequests.IgnoreQueryFilters()
            .Include(r => r.Documents)
            .FirstOrDefaultAsync(r => r.Documents.Any(d => d.FileId == fileId), ct);
}
