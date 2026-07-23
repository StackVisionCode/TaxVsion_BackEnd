using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

public sealed class SignatureRequestRepository(SignatureDbContext db) : ISignatureRequestRepository
{
    // Bug real de producción (RBAC/Wolverine scope, ver LocalCommandTenantMiddleware.cs): tenantId
    // ya viene explícito y validado desde el caller — IgnoreQueryFilters() porque el filtro
    // ambiental global puede no estar poblado en el scope de DI del handler de Wolverine.
    public Task<SignatureRequest?> GetByIdAsync(Guid tenantId, Guid requestId, CancellationToken ct = default) =>
        db
            .SignatureRequests.IgnoreQueryFilters()
            .Include(request => request.Signers)
                .ThenInclude(signer => signer.Fields)
            .Include(request => request.Signers)
                .ThenInclude(signer => signer.Challenges)
            .FirstOrDefaultAsync(request => request.Id == requestId && request.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<SignatureRequest>> ListDraftsWaitingForFileAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken ct = default
    ) =>
        await db
            .SignatureRequests.IgnoreQueryFilters()
            .Where(r =>
                r.TenantId == tenantId && r.OriginalFileId == fileId && r.Status == SignatureRequestStatus.Draft
            )
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SignatureRequest>> ListExpiredCandidatesAsync(
        DateTime nowUtc,
        CancellationToken ct = default
    ) =>
        await db
            .SignatureRequests.IgnoreQueryFilters()
            .Include(r => r.Signers)
            .Where(r =>
                r.ExpiresAtUtc <= nowUtc
                && (
                    r.Status == SignatureRequestStatus.Draft
                    || r.Status == SignatureRequestStatus.Ready
                    || r.Status == SignatureRequestStatus.InProgress
                )
            )
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SignatureRequest>> ListReminderCandidatesAsync(
        DateTime nowUtc,
        TimeSpan withinWindow,
        TimeSpan cooldown,
        int maxReminders,
        CancellationToken ct = default
    )
    {
        var cutoffExpires = nowUtc.Add(withinWindow);
        var cutoffLastReminder = nowUtc.Subtract(cooldown);
        return await db
            .SignatureRequests.IgnoreQueryFilters()
            .Include(r => r.Signers)
            .Where(r =>
                r.Status == SignatureRequestStatus.InProgress
                && r.ExpiresAtUtc > nowUtc
                && r.ExpiresAtUtc <= cutoffExpires
                && r.RemindersSent < maxReminders
                && (r.LastReminderSentAtUtc == null || r.LastReminderSentAtUtc <= cutoffLastReminder)
            )
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SignatureRequest>> ListPurgeCandidatesAsync(
        DateTime olderThanUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await db
            .SignatureRequests.IgnoreQueryFilters()
            .Where(r =>
                !r.LegalHold
                && (
                    r.Status == SignatureRequestStatus.Completed
                    || r.Status == SignatureRequestStatus.Rejected
                    || r.Status == SignatureRequestStatus.Canceled
                    || r.Status == SignatureRequestStatus.Expired
                )
                && r.UpdatedAtUtc <= olderThanUtc
            )
            .OrderBy(r => r.UpdatedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task AddAsync(SignatureRequest request, CancellationToken ct = default) =>
        await db.SignatureRequests.AddAsync(request, ct);

    public void Remove(SignatureRequest request) => db.SignatureRequests.Remove(request);
}
