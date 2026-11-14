using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using TaxVision.FinancialInsights.Infrastructure.Persistence;

namespace TaxVision.FinancialInsights.Api.Events;

// The file must be linked to a batch before a scan event can change its state.
// EventId and FileId remain stable across RabbitMQ redelivery; the domain transition is idempotent.
public static class FileScanConsumer
{
    public static async Task Handle(FileAvailableIntegrationEvent evt, FinancialInsightsDbContext db, CancellationToken ct)
    {
        if (!string.Equals(evt.OwnerType, "Customer", StringComparison.OrdinalIgnoreCase) || evt.OwnerId is null)
            return;
        var document = await db.ImportDocuments.SingleOrDefaultAsync(x => x.TenantId == evt.TenantId && x.FileId == evt.FileId, ct);
        if (document is null) return;
        var batch = await db.ImportBatches.SingleOrDefaultAsync(x => x.Id == document.BatchId && x.CustomerId == evt.OwnerId, ct);
        if (batch is null || !document.MarkReady()) return;
        await db.SaveChangesAsync(ct);
    }

    public static async Task Handle(FileInfectedDetectedIntegrationEvent evt, FinancialInsightsDbContext db, CancellationToken ct) =>
        await RejectAsync(evt.TenantId, evt.FileId, "Infected", db, ct);

    public static async Task Handle(FileScanFailedIntegrationEvent evt, FinancialInsightsDbContext db, CancellationToken ct) =>
        await RejectAsync(evt.TenantId, evt.FileId, "ScanFailed", db, ct);

    private static async Task RejectAsync(Guid tenantId, Guid fileId, string reason, FinancialInsightsDbContext db, CancellationToken ct)
    {
        var document = await db.ImportDocuments.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.FileId == fileId, ct);
        if (document?.Reject(reason) == true) await db.SaveChangesAsync(ct);
    }
}
