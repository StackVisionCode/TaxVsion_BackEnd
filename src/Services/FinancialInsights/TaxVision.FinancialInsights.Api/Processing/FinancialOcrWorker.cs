using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Infrastructure.Persistence;
using TaxVision.FinancialInsights.Infrastructure.Processing;

namespace TaxVision.FinancialInsights.Api.Processing;

/// <summary>Claims ready documents atomically, then performs OCR and model inference outside the SQL transaction.</summary>
public sealed class FinancialOcrWorker(IServiceScopeFactory scopes, IOptions<FinancialProcessingOptions> settings,
    IOptions<OpenAiFinancialOptions> openAi, ILogger<FinancialOcrWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Value.Enabled || string.IsNullOrWhiteSpace(openAi.Value.ApiKey))
        {
            logger.LogInformation("Financial OCR worker is idle: processing is disabled or OpenAI key is absent.");
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecoverInterruptedAsync(stoppingToken);
                await ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Exception messages from storage HTTP calls may contain signed URLs.
                logger.LogWarning("Financial OCR loop failed with {ExceptionType}.", ex.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(settings.Value.PollSeconds, 2, 300)), stoppingToken);
        }
    }

    private async Task RecoverInterruptedAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialInsightsDbContext>();
        await db.ImportDocuments.IgnoreQueryFilters()
            .Where(x => x.Status == ImportDocumentStatus.Processing
                && x.UpdatedAtUtc < DateTime.UtcNow.AddMinutes(-30))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, ImportDocumentStatus.Failed)
                .SetProperty(x => x.FailureReason, "Interrupted"), ct);
    }

    private async Task ProcessNextAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialInsightsDbContext>();
        var candidates = await db.ImportDocuments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Status == ImportDocumentStatus.Ready)
            .OrderBy(x => x.UpdatedAtUtc).Select(x => new { x.Id, x.TenantId }).Take(10).ToListAsync(ct);
        foreach (var candidate in candidates)
        {
            var claimed = await db.ImportDocuments.IgnoreQueryFilters()
                .Where(x => x.Id == candidate.Id && x.TenantId == candidate.TenantId && x.Status == ImportDocumentStatus.Ready)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, ImportDocumentStatus.Processing)
                    .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), ct);
            if (claimed != 1) continue;
            await ProcessClaimedAsync(candidate.Id, candidate.TenantId, scope.ServiceProvider, ct);
            return; // One document per poll keeps local OCR and model costs bounded.
        }
    }

    private async Task ProcessClaimedAsync(Guid documentId, Guid tenantId, IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<FinancialInsightsDbContext>();
        var document = await db.ImportDocuments.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == documentId && x.TenantId == tenantId, ct);
        var batch = await db.ImportBatches.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == document.BatchId && x.TenantId == tenantId, ct);
        try
        {
            if (batch is null) throw new DocumentProcessingException("BatchMissing", "Import batch is missing.");
            var tenant = await db.TenantDirectoryProjections.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
            var customer = await db.CustomerDirectoryProjections.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.CustomerId == batch.CustomerId, ct);
            var entitlement = await db.TenantEntitlementsProjections.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
            if (tenant is { IsActive: false } || customer is not { IsActive: true }
                || entitlement is null || !entitlement.EnabledModules.Contains("financial_insights", StringComparer.OrdinalIgnoreCase))
                throw new DocumentProcessingException("AccessChanged", "Tenant, customer or module is no longer active.");

            var file = await services.GetRequiredService<CloudStorageStatementSource>()
                .DownloadAsync(tenantId, batch.CustomerId, document.FileId, ct);
            var pages = await services.GetRequiredService<LocalStatementExtractor>()
                .ExtractAsync(file.Bytes, file.MediaType, ct);
            var reservation = await ReserveAsync(db, tenantId, documentId, openAi.Value.Model,
                settings.Value.MaxDocumentsPerTenantMonth, ct);
            if (reservation is null)
                throw new DocumentProcessingException("TenantAiLimit", "Tenant monthly document quota is exhausted.");
            var inference = await services.GetRequiredService<OpenAiStatementClient>().AnalyzeAsync(pages, ct);
            var needsReview = inference.NeedsReview || inference.Movements.Count == 0;
            foreach (var movement in inference.Movements)
            {
                if (!DateOnly.TryParseExact(movement.TransactionDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date) || movement.SourcePage > pages.Count)
                { needsReview = true; continue; }
                try
                {
                    db.FinancialMovements.Add(FinancialMovement.Create(tenantId, batch.CustomerId, documentId,
                        date, movement.Description, movement.Amount, movement.Currency, movement.Direction,
                        movement.SuggestedCategory, movement.SourcePage));
                }
                catch (ArgumentException) { needsReview = true; }
            }
            reservation.RecordUsage(inference.InputTokens, inference.OutputTokens);
            document.Complete(needsReview);
            await db.SaveChangesAsync(ct);
        }
        catch (DocumentProcessingException ex)
        {
            if (ex.Code is "VisionPageLimit" or "DocumentLimit" or "NoReadableContent" or "AccessChanged" or "TenantAiLimit")
                document.HoldForReview(ex.Code);
            else document.Fail(ex.Code);
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Financial document processing stopped: {Code}.", ex.Code);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is not DbUpdateException)
        {
            foreach (var entry in db.ChangeTracker.Entries<FinancialMovement>()
                .Where(entry => entry.State == EntityState.Added))
                entry.State = EntityState.Detached;
            document.Fail("ProcessingError");
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Financial document processing failed with {ExceptionType}.", ex.GetType().Name);
        }
    }

    private static async Task<AiProcessingReservation?> ReserveAsync(FinancialInsightsDbContext db,
        Guid tenantId, Guid documentId, string model, int monthlyLimit, CancellationToken ct)
    {
        if (monthlyLimit < 1) return null;
        var now = DateTime.UtcNow;
        var month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var existing = await db.AiProcessingReservations.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ImportDocumentId == documentId, ct);
        if (existing is not null) return null; // Avoid automatic repeat billing after a previous attempt.
        var used = await db.AiProcessingReservations.IgnoreQueryFilters()
            .CountAsync(x => x.TenantId == tenantId && x.MonthStartUtc == month, ct);
        if (used >= monthlyLimit) return null;
        var reservation = AiProcessingReservation.Create(tenantId, documentId, model, now);
        db.AiProcessingReservations.Add(reservation);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return reservation;
    }
}
