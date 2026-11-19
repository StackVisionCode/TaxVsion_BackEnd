using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

/// <summary>Asuntos por id con <see cref="MailVisibilityQueries"/>: una consulta por tipo, sin N+1.</summary>
public sealed class MailTitleReader(CorrespondenceDbContext db) : IMailTitleReader
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetSentSubjectsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> draftIds,
        IReadOnlyCollection<Guid>? visibleAccountIds,
        Guid? restrictToUserId,
        CancellationToken ct = default
    )
    {
        var ids = MailVisibilityQueries.AsArray(draftIds);
        var drafts = db
            .Drafts.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(d =>
                d.TenantId == tenantId && ids.Contains(d.Id) && d.Status == DraftStatus.Sent && d.DeletedAtUtc == null
            );
        // Mismo alcance que GET /sent?scope=all.
        drafts = MailVisibilityQueries.OutgoingInScope(
            db,
            drafts,
            DraftCustomerScope.All.RestrictedTo(restrictToUserId)
        );
        drafts = MailVisibilityQueries.FromVisibleMailbox(drafts, visibleAccountIds);
        return await drafts.Select(d => new { d.Id, d.Subject }).ToDictionaryAsync(x => x.Id, x => x.Subject, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetThreadSubjectsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> threadIds,
        IReadOnlyCollection<Guid>? visibleAccountIds,
        Guid? restrictToUserId,
        CancellationToken ct = default
    )
    {
        var ids = MailVisibilityQueries.AsArray(threadIds);
        return await MailVisibilityQueries
            .VisibleThreads(db, tenantId, visibleAccountIds, restrictToUserId)
            .Where(t => ids.Contains(t.Id))
            .Select(t => new { t.Id, t.Subject })
            .ToDictionaryAsync(x => x.Id, x => x.Subject, ct);
    }
}
