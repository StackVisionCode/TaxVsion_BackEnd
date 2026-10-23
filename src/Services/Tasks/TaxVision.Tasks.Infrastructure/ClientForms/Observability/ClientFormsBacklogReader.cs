using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Infrastructure.Observability;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Observability;

/// <summary>
/// Conteos de todos los tenants para los gauges. Corre en un job periódico y no por scrape: cada
/// lectura son cinco COUNT, y los de adjuntos y runs van por sus índices (Status, fecha).
/// </summary>
public sealed class ClientFormsBacklogReader(TasksDbContext db)
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    public async Task<ClientFormsBacklog> ReadAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var staleCutoff = nowUtc - StaleAfter;
        var oneDayAgo = nowUtc.AddDays(-1);
        var threeDaysAgo = nowUtc.AddDays(-3);
        var awaiting = db
            .FormRequests.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(request => request.Status == FormRequestStatus.Submitted);
        return new ClientFormsBacklog(
            await db
                .FormAttachments.IgnoreQueryFilters()
                .AsNoTracking()
                .LongCountAsync(
                    attachment =>
                        attachment.Status == FormAttachmentStatus.Pending && attachment.RegisteredAtUtc < staleCutoff,
                    ct
                ),
            await awaiting.LongCountAsync(request => request.LastSubmittedAtUtc >= oneDayAgo, ct),
            await awaiting.LongCountAsync(
                request => request.LastSubmittedAtUtc < oneDayAgo && request.LastSubmittedAtUtc >= threeDaysAgo,
                ct
            ),
            await awaiting.LongCountAsync(
                request => request.LastSubmittedAtUtc == null || request.LastSubmittedAtUtc < threeDaysAgo,
                ct
            ),
            await db
                .FormActionRuns.IgnoreQueryFilters()
                .AsNoTracking()
                .LongCountAsync(
                    run => run.Status == FormActionRunStatus.Requested && run.RequestedAtUtc < staleCutoff,
                    ct
                )
        );
    }
}
