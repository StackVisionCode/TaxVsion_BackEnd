using System.Data;
using Microsoft.EntityFrameworkCore;
using TaxVision.FinancialInsights.Application.Imports;
using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Domain.Projections;
using TaxVision.FinancialInsights.Infrastructure.Persistence;

namespace TaxVision.FinancialInsights.Infrastructure.Processing;

public sealed record ClaimCandidate(Guid DocumentId, Guid TenantId);

/// <summary>Cola del worker sobre ImportDocuments. Lecturas y la toma atómica; el resto pasa por el agregado.</summary>
public sealed class ImportProcessingQueue(FinancialInsightsDbContext db)
{
    /// <summary>Documentos listos de tenants con plan local. Sin fila de plan esperan, no se rechazan.</summary>
    public async Task<IReadOnlyList<ClaimCandidate>> ListClaimCandidatesAsync(int take, CancellationToken ct) =>
        await db
            .ImportDocuments.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(d =>
                d.Status == ImportDocumentStatus.Ready
                && db.TenantEntitlementsProjections.IgnoreQueryFilters().Any(e => e.TenantId == d.TenantId)
            )
            .OrderBy(d => d.UpdatedAtUtc)
            .Take(take)
            .Select(d => new ClaimCandidate(d.Id, d.TenantId))
            .ToListAsync(ct);

    /// <summary>
    /// Toma atómica Ready → Processing (el mismo paso que <c>ImportBatch.StartProcessing</c>) en un solo UPDATE
    /// condicionado, para que dos workers nunca tomen el mismo documento.
    /// </summary>
    public async Task<bool> TryClaimAsync(ClaimCandidate candidate, CancellationToken ct) =>
        await db
            .ImportDocuments.IgnoreQueryFilters()
            .Where(d =>
                d.Id == candidate.DocumentId
                && d.TenantId == candidate.TenantId
                && d.Status == ImportDocumentStatus.Ready
            )
            .ExecuteUpdateAsync(
                set => set.SetProperty(d => d.Status, ImportDocumentStatus.Processing)
                    .SetProperty(d => d.UpdatedAtUtc, DateTime.UtcNow),
                ct
            ) == 1;

    /// <summary>Documentos que quedaron en Processing por un worker caído.</summary>
    public async Task<IReadOnlyList<ClaimCandidate>> ListInterruptedAsync(DateTime olderThanUtc, int take, CancellationToken ct) =>
        await db
            .ImportDocuments.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(d => d.Status == ImportDocumentStatus.Processing && d.UpdatedAtUtc < olderThanUtc)
            .OrderBy(d => d.UpdatedAtUtc)
            .Take(take)
            .Select(d => new ClaimCandidate(d.Id, d.TenantId))
            .ToListAsync(ct);

    /// <summary>Tenants con documentos listos que esperan la fila del plan (el worker intenta repararla).</summary>
    public async Task<IReadOnlyList<Guid>> ListTenantsWaitingForPlanAsync(int take, CancellationToken ct) =>
        await db
            .ImportDocuments.IgnoreQueryFilters()
            .Where(d =>
                d.Status == ImportDocumentStatus.Ready
                && !db.TenantEntitlementsProjections.IgnoreQueryFilters().Any(e => e.TenantId == d.TenantId)
            )
            .Select(d => d.TenantId)
            .Distinct()
            .Take(take)
            .ToListAsync(ct);

    /// <summary>
    /// Tenants conocidos localmente (directorio de oficinas o con lotes) sin fila de plan o con una anterior a
    /// <paramref name="staleBeforeUtc"/>. Primero los que no tienen fila.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> ListTenantsToRefreshAsync(DateTime staleBeforeUtc, int take, CancellationToken ct)
    {
        var known = db
            .TenantDirectoryProjections.IgnoreQueryFilters()
            .Select(t => t.TenantId)
            .Union(db.ImportBatches.IgnoreQueryFilters().Select(b => b.TenantId));
        var plans = db.TenantEntitlementsProjections.IgnoreQueryFilters();
        return await known
            .Where(tenantId => !plans.Any(e => e.TenantId == tenantId && e.UpdatedAtUtc >= staleBeforeUtc))
            .OrderBy(tenantId => plans.Any(e => e.TenantId == tenantId))
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<ProcessingContext?> LoadContextAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        var batch = await db
            .ImportBatches.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(b => b.TenantId == tenantId && b.Documents.Any(d => d.Id == documentId))
            .Select(b => new { b.CustomerId, FileId = b.Documents.Where(d => d.Id == documentId).Select(d => d.FileId).First() })
            .SingleOrDefaultAsync(ct);
        if (batch is null)
            return null;

        var tenant = await db.TenantDirectoryProjections.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(t => t.TenantId == tenantId, ct);
        var customer = await db.CustomerDirectoryProjections.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(c => c.TenantId == tenantId && c.CustomerId == batch.CustomerId, ct);
        var plan = await db.TenantEntitlementsProjections.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(e => e.TenantId == tenantId, ct);
        return new ProcessingContext(batch.CustomerId, batch.FileId, tenant, customer, plan);
    }

    /// <summary>
    /// Reserva serializable del cupo mensual antes de llamar al modelo. <c>false</c> si el documento ya tuvo un intento
    /// (no se cobra dos veces) o si el tenant agotó su cupo del mes.
    /// </summary>
    public async Task<bool> TryReserveAsync(Guid tenantId, Guid documentId, string model, int monthlyLimit, CancellationToken ct)
    {
        if (monthlyLimit < 1)
            return false;
        var now = DateTime.UtcNow;
        var month = AiProcessingReservation.MonthStart(now);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var reservations = db.AiProcessingReservations.IgnoreQueryFilters();
        if (await reservations.AnyAsync(r => r.TenantId == tenantId && r.ImportDocumentId == documentId, ct))
            return false;
        if (await reservations.CountAsync(r => r.TenantId == tenantId && r.MonthStartUtc == month, ct) >= monthlyLimit)
            return false;
        var reservation = AiProcessingReservation.Create(tenantId, documentId, model, now);
        if (reservation.IsFailure)
            return false;
        db.AiProcessingReservations.Add(reservation.Value);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return true;
    }
}

public sealed record ProcessingContext(
    Guid CustomerId,
    Guid FileId,
    TenantDirectoryProjection? Tenant,
    CustomerDirectoryProjection? Customer,
    TenantEntitlementsProjection? Plan
);

public enum DocumentAccess
{
    Allowed,
    Pending,
    Revoked,
}

/// <summary>
/// Misma regla que el API: oficina activa, cliente activo y módulo habilitado. Una fila de plan ausente no es un
/// rechazo (el API no habría aceptado el lote): el documento vuelve a la cola hasta que llegue el plan.
/// </summary>
public static class DocumentProcessingAccess
{
    public static DocumentAccess Evaluate(
        TenantDirectoryProjection? tenant,
        CustomerDirectoryProjection? customer,
        TenantEntitlementsProjection? plan
    )
    {
        if (tenant is { IsActive: false } || customer is not { IsActive: true })
            return DocumentAccess.Revoked;
        if (plan is null)
            return DocumentAccess.Pending;
        return plan.HasModule(FinancialEntitlements.Module) ? DocumentAccess.Allowed : DocumentAccess.Revoked;
    }
}
