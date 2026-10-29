using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories;

internal sealed class ConsentNoticeRepository(TasksDbContext context) : IConsentNoticeRepository
{
    public Task<ConsentNotice?> GetAsync(Guid tenantId, Guid noticeId, CancellationToken ct = default) =>
        context
            .ConsentNotices.IgnoreQueryFilters()
            .Include(item => item.Versions)
            .SingleOrDefaultAsync(item => item.TenantId == tenantId && item.Id == noticeId, ct);

    public Task<ConsentNotice?> GetByKeyAsync(Guid tenantId, string key, CancellationToken ct = default) =>
        context
            .ConsentNotices.IgnoreQueryFilters()
            .Include(item => item.Versions)
            .SingleOrDefaultAsync(item => item.TenantId == tenantId && item.Key == key.Trim().ToLower(), ct);

    public Task<ConsentNoticeVersion?> GetVersionAsync(
        Guid tenantId,
        Guid noticeId,
        int versionNumber,
        CancellationToken ct = default
    ) =>
        context
            .ConsentNoticeVersions.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.TenantId == tenantId
                    && item.ConsentNoticeId == noticeId
                    && item.VersionNumber == versionNumber,
                ct
            );

    public async Task<IReadOnlyList<ConsentNotice>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
        await context
            .ConsentNotices.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(item => item.Versions)
            .Where(item => item.TenantId == tenantId)
            .OrderBy(item => item.Key)
            .ToArrayAsync(ct);

    public void Add(ConsentNotice notice) => context.ConsentNotices.Add(notice);
}

internal sealed class FormConsentRecordRepository(TasksDbContext context) : IFormConsentRecordRepository
{
    public Task<FormConsentRecord?> GetAsync(FormConsentRecordKey key, CancellationToken ct = default) =>
        context
            .FormConsentRecords.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                item =>
                    item.TenantId == key.TenantId
                    && item.FormRequestId == key.RequestId
                    && item.ComponentId == key.ComponentId
                    && item.ParticipantKind == key.ParticipantKind
                    && item.CustomerId == key.CustomerId
                    && item.ParticipantId == key.ParticipantId
                    && item.ConsentNoticeId == key.NoticeId
                    && item.NoticeVersionNumber == key.NoticeVersionNumber
                    && item.Outcome == key.Outcome,
                ct
            );

    public async Task<IReadOnlyList<FormConsentRecord>> ListForRequestAsync(
        Guid tenantId,
        Guid requestId,
        CancellationToken ct = default
    ) =>
        await context
            .FormConsentRecords.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.FormRequestId == requestId)
            .OrderBy(item => item.RecordedAtUtc)
            .ToArrayAsync(ct);

    public void Add(FormConsentRecord record) => context.FormConsentRecords.Add(record);

    public void Discard(FormConsentRecord record)
    {
        var entry = context.Entry(record);
        if (entry.Reference(item => item.AcceptedBy).TargetEntry is { } actor)
            actor.State = EntityState.Detached;
        entry.State = EntityState.Detached;
    }
}
