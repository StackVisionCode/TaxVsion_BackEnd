using BuildingBlocks.CustomerVisibility;
using Microsoft.EntityFrameworkCore;
using TaxVision.FinancialInsights.Application.Imports;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;
using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Domain.Projections;

namespace TaxVision.FinancialInsights.Infrastructure.Persistence.Repositories;

public sealed class FinancialMovementRepository(FinancialInsightsDbContext db) : IFinancialMovementRepository
{
    public async Task<IReadOnlyList<FinancialMovementView>> ListByDocumentsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default
    ) =>
        await db
            .FinancialMovements.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && documentIds.Contains(m.ImportDocumentId))
            .OrderBy(m => m.TransactionDate)
            .ThenBy(m => m.SourcePage)
            .Select(m => new FinancialMovementView(
                m.Id,
                m.ImportDocumentId,
                m.TransactionDate,
                m.Description,
                m.Amount,
                m.Currency,
                m.Direction,
                m.SuggestedCategory,
                m.SourcePage,
                m.RequiresReview
            ))
            .ToListAsync(ct);

    public void Add(FinancialMovement movement) => db.FinancialMovements.Add(movement);
}

public sealed class AiReservationRepository(FinancialInsightsDbContext db) : IAiReservationRepository
{
    public Task<bool> ExistsForDocumentAsync(Guid tenantId, Guid documentId, CancellationToken ct = default) =>
        db.AiProcessingReservations.IgnoreQueryFilters()
            .AnyAsync(r => r.TenantId == tenantId && r.ImportDocumentId == documentId, ct);

    public Task<AiProcessingReservation?> GetForDocumentAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken ct = default
    ) =>
        db.AiProcessingReservations.IgnoreQueryFilters()
            .SingleOrDefaultAsync(r => r.TenantId == tenantId && r.ImportDocumentId == documentId, ct);
}

public sealed class FinancialAccessReader(FinancialInsightsDbContext db) : IFinancialAccessReader
{
    public Task<TenantDirectoryProjection?> FindTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        db.TenantDirectoryProjections.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(t => t.TenantId == tenantId, ct);

    public Task<CustomerDirectoryProjection?> FindCustomerAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) =>
        db.CustomerDirectoryProjections.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.TenantId == tenantId && c.CustomerId == customerId, ct);

    public Task<bool> IsCustomerAssignedAsync(
        Guid tenantId,
        Guid customerId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        db.Set<CustomerAssignmentProjection>()
            .IgnoreQueryFilters()
            .AnyAsync(a => a.TenantId == tenantId && a.CustomerId == customerId && a.UserId == userId, ct);
}
