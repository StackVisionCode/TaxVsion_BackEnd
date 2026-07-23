using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Analytics;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Infrastructure.Persistence.Queries;

/// <summary>
/// Read service optimizado para el dashboard staff. Cada endpoint tiene su método
/// privado que expresa la regla concreta (suma, series de tiempo, agrupación por
/// categoría). No mezcla lógica; se apoya en LINQ traducible a SQL agrupado.
/// </summary>
internal sealed class SignatureAnalyticsReadService(SignatureDbContext db) : ISignatureAnalyticsReadService
{
    public async Task<SignatureAnalyticsSummary> GetSummaryAsync(
        SignatureAnalyticsSummaryQuery query,
        CancellationToken ct = default
    )
    {
        var (from, to) = ClampRange(query.FromDay, query.ToDay);

        var totals = await db
            .SignatureAnalyticsSnapshots.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == query.TenantId && s.Day >= from && s.Day <= to)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Created = g.Sum(s => s.RequestsCreated),
                Sent = g.Sum(s => s.RequestsSent),
                Canceled = g.Sum(s => s.RequestsCanceled),
                Expired = g.Sum(s => s.RequestsExpired),
                Completed = g.Sum(s => s.RequestsCompleted),
                Sealed = g.Sum(s => s.RequestsSealed),
                SignersSigned = g.Sum(s => s.SignersSigned),
                SignersRejected = g.Sum(s => s.SignersRejected),
            })
            .FirstOrDefaultAsync(ct);

        var t =
            totals
            ?? new
            {
                Created = 0,
                Sent = 0,
                Canceled = 0,
                Expired = 0,
                Completed = 0,
                Sealed = 0,
                SignersSigned = 0,
                SignersRejected = 0,
            };

        return new SignatureAnalyticsSummary(
            query.TenantId,
            from,
            to,
            t.Created,
            t.Sent,
            t.Canceled,
            t.Expired,
            t.Completed,
            t.Sealed,
            t.SignersSigned,
            t.SignersRejected,
            ComputeCompletionRate(t.Sent, t.Completed),
            ComputeRejectionRate(t.SignersSigned, t.SignersRejected)
        );
    }

    public async Task<SignatureAnalyticsTimeline> GetTimelineAsync(
        SignatureAnalyticsTimelineQuery query,
        CancellationToken ct = default
    )
    {
        var (from, to) = ClampRange(query.FromDay, query.ToDay);

        var raw = await db
            .SignatureAnalyticsSnapshots.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == query.TenantId && s.Day >= from && s.Day <= to)
            .GroupBy(s => s.Day)
            .Select(g => new
            {
                Day = g.Key,
                Created = g.Sum(s => s.RequestsCreated),
                Sent = g.Sum(s => s.RequestsSent),
                Completed = g.Sum(s => s.RequestsCompleted),
                SignersSigned = g.Sum(s => s.SignersSigned),
                SignersRejected = g.Sum(s => s.SignersRejected),
            })
            .ToListAsync(ct);

        var byDay = raw.ToDictionary(r => r.Day);
        var points = new List<SignatureAnalyticsTimelinePoint>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (byDay.TryGetValue(d, out var row))
                points.Add(
                    new SignatureAnalyticsTimelinePoint(
                        d,
                        row.Created,
                        row.Sent,
                        row.Completed,
                        row.SignersSigned,
                        row.SignersRejected
                    )
                );
            else
                points.Add(new SignatureAnalyticsTimelinePoint(d, 0, 0, 0, 0, 0));
        }
        return new SignatureAnalyticsTimeline(from, to, points);
    }

    public async Task<SignatureAnalyticsByCategory> GetByCategoryAsync(
        SignatureAnalyticsByCategoryQuery query,
        CancellationToken ct = default
    )
    {
        var (from, to) = ClampRange(query.FromDay, query.ToDay);

        var entries = await db
            .SignatureAnalyticsSnapshots.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == query.TenantId && s.Day >= from && s.Day <= to)
            .GroupBy(s => s.Category)
            .Select(g => new SignatureAnalyticsByCategoryEntry(
                g.Key,
                g.Sum(s => s.RequestsCreated),
                g.Sum(s => s.RequestsCompleted),
                g.Sum(s => s.RequestsCanceled)
            ))
            .ToListAsync(ct);

        return new SignatureAnalyticsByCategory(from, to, entries);
    }

    // ------------------------------------------------------------------
    // Métodos privados: una única responsabilidad
    // ------------------------------------------------------------------

    private static (DateOnly From, DateOnly To) ClampRange(DateOnly requestedFrom, DateOnly requestedTo)
    {
        var from = requestedFrom;
        var to = requestedTo;
        if (to < from)
            (from, to) = (to, from);
        return (from, to);
    }

    private static double ComputeCompletionRate(int sent, int completed) =>
        sent <= 0 ? 0d : Math.Round((double)completed / sent, 4);

    private static double ComputeRejectionRate(int signed, int rejected)
    {
        var total = signed + rejected;
        return total <= 0 ? 0d : Math.Round((double)rejected / total, 4);
    }
}
