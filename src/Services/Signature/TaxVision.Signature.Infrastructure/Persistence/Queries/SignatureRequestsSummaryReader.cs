using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Infrastructure.Persistence.Queries;

/// <summary>Conteos del widget de firmas agregados en SQL, con el mismo recorte que el listado.</summary>
internal sealed class SignatureRequestsSummaryReader(SignatureDbContext db) : ISignatureRequestsSummaryReader
{
    public async Task<SignatureRequestsLiveCounts> ReadAsync(
        SignatureRequestsSummaryFilter filter,
        CancellationToken ct = default
    )
    {
        // Scope de Wolverine sin tenant ambiental: tenant explícito e IgnoreQueryFilters.
        var scope = db.SignatureRequests.AsNoTracking().IgnoreQueryFilters().Where(r => r.TenantId == filter.TenantId);
        if (filter.AssignedTo is { } assignee)
            scope = scope.VisibleTo(db, filter.TenantId, assignee);
        // CreatedByUserId va en el INCLUDE del índice del resumen: no hace falta otro.
        if (filter.CreatedBy is { } author)
            scope = scope.Where(r => r.CreatedByUserId == author);

        var (from, to, now) = (filter.FromUtc, filter.ToUtc, filter.NowUtc);
        var soon = now.Add(SignatureRequestListFilters.ExpiringSoonWindow);
        var counts = await scope
            .GroupBy(_ => 1)
            .Select(g => new SignatureRequestsLiveCounts(
                g.Count(r => r.Status == SignatureRequestStatus.Draft),
                g.Count(r => r.Status == SignatureRequestStatus.InProgress),
                g.Count(r =>
                    r.Status == SignatureRequestStatus.InProgress && r.ExpiresAtUtc > now && r.ExpiresAtUtc <= soon
                ),
                g.Count(r => r.SentAtUtc >= from && r.SentAtUtc < to),
                g.Count(r =>
                    r.Status == SignatureRequestStatus.Completed && r.CompletedAtUtc >= from && r.CompletedAtUtc < to
                ),
                g.Count(r =>
                    r.Status == SignatureRequestStatus.Expired && r.ExpiredAtUtc >= from && r.ExpiredAtUtc < to
                ),
                g.Count(r =>
                    r.Status == SignatureRequestStatus.Canceled && r.CanceledAtUtc >= from && r.CanceledAtUtc < to
                ),
                null
            ))
            .FirstOrDefaultAsync(ct);

        // Media envío→firma de las completadas del período; DATEDIFF solo existe en SQL Server.
        var avg = await scope
            .Where(r =>
                r.Status == SignatureRequestStatus.Completed
                && r.CompletedAtUtc >= from
                && r.CompletedAtUtc < to
                && r.SentAtUtc != null
            )
            .Select(r => (double?)EF.Functions.DateDiffMinute(r.SentAtUtc, r.CompletedAtUtc))
            .AverageAsync(ct);

        return (counts ?? new SignatureRequestsLiveCounts(0, 0, 0, 0, 0, 0, 0, null)) with
        {
            AvgMinutesToSign = avg is { } minutes ? Math.Round(minutes, 1) : null,
        };
    }
}
