using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Invitations;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

public sealed class InvitationRepository(AuthDbContext db) : IInvitationRepository
{
    // IgnoreQueryFilters(): mismo bug que UserRepository.GetByIdAsync (ver su comentario) — los
    // 2 llamadores (CancelInvitation/ResendInvitation) ya validan invitation.TenantId contra el
    // tenant del actor/comando post-fetch, así que el filtro ambiental era redundante.
    public Task<Invitation?> GetByIdAsync(Guid invitationId, CancellationToken ct = default) =>
        db.Invitations.IgnoreQueryFilters().FirstOrDefaultAsync(invitation => invitation.Id == invitationId, ct);

    public Task<Invitation?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.Invitations.FirstOrDefaultAsync(invitation => invitation.TokenHash == tokenHash, ct);

    public Task<bool> HasPendingAsync(Guid tenantId, string email, CancellationToken ct = default) =>
        db
            .Invitations.IgnoreQueryFilters()
            .AnyAsync(
                invitation =>
                    invitation.TenantId == tenantId
                    && invitation.Email == email
                    && invitation.Status == InvitationStatus.Pending
                    && invitation.ExpiresAtUtc > DateTime.UtcNow,
                ct
            );

    public async Task AddAsync(Invitation invitation, CancellationToken ct = default) =>
        await db.Invitations.AddAsync(invitation, ct);

    public Task<int> CountPendingAsync(Guid tenantId, CancellationToken ct = default) =>
        db
            .Invitations.IgnoreQueryFilters()
            .CountAsync(
                invitation =>
                    invitation.TenantId == tenantId
                    && invitation.Status == InvitationStatus.Pending
                    && invitation.ExpiresAtUtc > DateTime.UtcNow,
                ct
            );

    public async Task<(IReadOnlyList<Invitation> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId,
        InvitationStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var query = db
            .Invitations.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(invitation => invitation.TenantId == tenantId);

        if (status is not null)
            query = query.Where(invitation => invitation.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(invitation => invitation.CreatedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return (items, total);
    }
}
