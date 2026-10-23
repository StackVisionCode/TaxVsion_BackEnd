using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories;

internal sealed class FormParticipantRepository(TasksDbContext context) : IFormParticipantRepository
{
    public Task<FormParticipant?> GetAsync(Guid tenantId, Guid participantId, CancellationToken ct = default) =>
        context
            .FormParticipants.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.TenantId == tenantId && item.Id == participantId, ct);

    public Task<FormParticipant?> GetUnlinkedByEmailAsync(
        Guid tenantId,
        string email,
        CancellationToken ct = default
    ) =>
        context
            .FormParticipants.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                item =>
                    item.TenantId == tenantId && item.Email == email.Trim().ToLower() && item.LinkedCustomerId == null,
                ct
            );

    public void Add(FormParticipant participant) => context.FormParticipants.Add(participant);

    public void Untrack(FormParticipant participant) => context.Entry(participant).State = EntityState.Detached;
}

internal sealed class FormIntakeLinkRepository(TasksDbContext context) : IFormIntakeLinkRepository
{
    public Task<FormIntakeLink?> GetAsync(Guid tenantId, Guid linkId, CancellationToken ct = default) =>
        context
            .FormIntakeLinks.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.TenantId == tenantId && item.Id == linkId, ct);

    public Task<FormIntakeLink?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        context.FormIntakeLinks.IgnoreQueryFilters().SingleOrDefaultAsync(item => item.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<FormIntakeLink>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
        await context
            .FormIntakeLinks.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToArrayAsync(ct);

    public void Add(FormIntakeLink link) => context.FormIntakeLinks.Add(link);
}

internal sealed class FormAccessGrantRepository(TasksDbContext context) : IFormAccessGrantRepository
{
    public Task<FormAccessGrant?> GetByRequestAsync(Guid tenantId, Guid requestId, CancellationToken ct = default) =>
        context
            .FormAccessGrants.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.FormRequestId == requestId)
            .OrderByDescending(item => item.IssuedAtUtc)
            .FirstOrDefaultAsync(ct);

    public Task<FormAccessGrant?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        context.FormAccessGrants.IgnoreQueryFilters().SingleOrDefaultAsync(item => item.TokenHash == tokenHash, ct);

    public void Add(FormAccessGrant grant) => context.FormAccessGrants.Add(grant);
}

internal sealed class FormAccessChallengeRepository(TasksDbContext context) : IFormAccessChallengeRepository
{
    public Task<FormAccessChallenge?> GetAsync(Guid tenantId, Guid challengeId, CancellationToken ct = default) =>
        context
            .FormAccessChallenges.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.TenantId == tenantId && item.Id == challengeId, ct);

    public Task<FormAccessChallenge?> GetLatestAsync(
        Guid tenantId,
        Guid? grantId,
        Guid? intakeLinkId,
        IReadOnlyCollection<string> emailLookupHashes,
        CancellationToken ct = default
    ) =>
        context
            .FormAccessChallenges.IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId
                && item.GrantId == grantId
                && item.IntakeLinkId == intakeLinkId
                && emailLookupHashes.Contains(item.EmailLookupHash)
            )
            .OrderByDescending(item => item.IssuedAtUtc)
            .FirstOrDefaultAsync(ct);

    public Task<int> CountIssuedAsync(
        Guid tenantId,
        IReadOnlyCollection<string> emailLookupHashes,
        DateTime sinceUtc,
        CancellationToken ct = default
    ) =>
        context
            .FormAccessChallenges.IgnoreQueryFilters()
            .CountAsync(
                item =>
                    item.TenantId == tenantId
                    && emailLookupHashes.Contains(item.EmailLookupHash)
                    && item.IssuedAtUtc >= sinceUtc,
                ct
            );

    public Task<int> CountTenantIssuedAsync(Guid tenantId, DateTime sinceUtc, CancellationToken ct = default) =>
        context
            .FormAccessChallenges.IgnoreQueryFilters()
            .CountAsync(item => item.TenantId == tenantId && item.IssuedAtUtc >= sinceUtc, ct);

    public Task<bool> HasActiveBlockAsync(
        Guid tenantId,
        Guid? grantId,
        Guid? intakeLinkId,
        IReadOnlyCollection<string> emailLookupHashes,
        DateTime nowUtc,
        CancellationToken ct = default
    ) =>
        context
            .FormAccessChallenges.IgnoreQueryFilters()
            .AnyAsync(
                item =>
                    item.TenantId == tenantId
                    && item.GrantId == grantId
                    && item.IntakeLinkId == intakeLinkId
                    && emailLookupHashes.Contains(item.EmailLookupHash)
                    && item.BlockedUntilUtc != null
                    && item.BlockedUntilUtc > nowUtc,
                ct
            );

    public void Add(FormAccessChallenge challenge) => context.FormAccessChallenges.Add(challenge);
}

internal sealed class FormAccessSessionRepository(TasksDbContext context) : IFormAccessSessionRepository
{
    public Task<FormAccessSession?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        context
            .FormAccessSessions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.SessionTokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<FormAccessSession>> ListNotRevokedByRequestAsync(
        Guid tenantId,
        Guid requestId,
        CancellationToken ct = default
    ) =>
        await context
            .FormAccessSessions.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.FormRequestId == requestId && item.RevokedAtUtc == null)
            .ToArrayAsync(ct);

    public void Add(FormAccessSession session) => context.FormAccessSessions.Add(session);
}
