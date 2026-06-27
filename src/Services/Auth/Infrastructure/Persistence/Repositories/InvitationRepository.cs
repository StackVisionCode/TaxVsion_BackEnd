using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Invitations;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

public sealed class InvitationRepository(AuthDbContext db) : IInvitationRepository
{
    public Task<Invitation?> GetByIdAsync(
        Guid invitationId,
        CancellationToken ct = default) =>
        db.Invitations.FirstOrDefaultAsync(
            invitation => invitation.Id == invitationId,
            ct);

    public Task<Invitation?> GetByTokenHashAsync(
        string tokenHash,
        CancellationToken ct = default) =>
        db.Invitations.FirstOrDefaultAsync(
            invitation => invitation.TokenHash == tokenHash,
            ct);

    public Task<bool> HasPendingAsync(
        Guid tenantId,
        string email,
        CancellationToken ct = default) =>
        db.Invitations.AnyAsync(
            invitation =>
                invitation.TenantId == tenantId &&
                invitation.Email == email &&
                invitation.Status == InvitationStatus.Pending &&
                invitation.ExpiresAtUtc > DateTime.UtcNow,
            ct);

    public async Task AddAsync(
        Invitation invitation,
        CancellationToken ct = default) =>
        await db.Invitations.AddAsync(invitation, ct);
}
