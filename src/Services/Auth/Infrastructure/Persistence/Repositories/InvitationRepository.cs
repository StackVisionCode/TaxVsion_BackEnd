using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

public sealed class InvitationRepository(AuthDbContext db) : IInvitationRepository
{
    // IgnoreQueryFilters(): mismo bug que UserRepository.GetByIdAsync (ver su comentario) — los
    // 2 llamadores (CancelInvitation/ResendInvitation) ya validan invitation.TenantId contra el
    // tenant del actor/comando post-fetch, así que el filtro ambiental era redundante.
    public Task<Invitation?> GetByIdAsync(Guid invitationId, CancellationToken ct = default) =>
        db.Invitations.IgnoreQueryFilters().FirstOrDefaultAsync(invitation => invitation.Id == invitationId, ct);

    // IgnoreQueryFilters(): el lookup por token-hash corre en AcceptInvitation ([AllowAnonymous]),
    // donde no hay contexto de tenant y el filtro global fail-closed devolvería 0 filas — bloqueando
    // el alta de todo admin nuevo. El TokenHash es SHA256 de 32 bytes aleatorios (globalmente único);
    // el handler valida MatchesTokenHash + tenant activo justo después del fetch, así que el filtro
    // ambiental era redundante aquí (mismo razonamiento que GetByIdAsync arriba).
    public Task<Invitation?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.Invitations.IgnoreQueryFilters().FirstOrDefaultAsync(invitation => invitation.TokenHash == tokenHash, ct);

    public Task<bool> HasPendingAsync(
        Guid tenantId,
        string email,
        UserAccountKind kind,
        CancellationToken ct = default
    ) => Pending(tenantId, email, kind).AnyAsync(ct);

    public Task<Invitation?> GetPendingAsync(
        Guid tenantId,
        string email,
        UserAccountKind kind,
        CancellationToken ct = default
    ) =>
        Pending(tenantId, email, kind).OrderByDescending(invitation => invitation.CreatedAtUtc).FirstOrDefaultAsync(ct);

    public async Task AddAsync(Invitation invitation, CancellationToken ct = default) =>
        await db.Invitations.AddAsync(invitation, ct);

    // Solo cuentan asiento las invitaciones de STAFF (TenantEmployee/TenantAdmin). Las invitaciones de
    // portal (clientes) NO consumen asientos del plan — se crean desde Clients y viven en su propio pool.
    public Task<int> CountPendingAsync(Guid tenantId, CancellationToken ct = default) =>
        db
            .Invitations.IgnoreQueryFilters()
            .CountAsync(
                invitation =>
                    invitation.TenantId == tenantId
                    && invitation.Status == InvitationStatus.Pending
                    && invitation.ExpiresAtUtc > DateTime.UtcNow
                    && (
                        invitation.ActorType == UserActorType.TenantEmployee
                        || invitation.ActorType == UserActorType.TenantAdmin
                    ),
                ct
            );

    public async Task<(IReadOnlyList<Invitation> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId,
        InvitationStatus? status,
        int page,
        int size,
        Guid? customerId = null,
        CancellationToken ct = default
    )
    {
        var query = db
            .Invitations.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(invitation => invitation.TenantId == tenantId);

        if (status is not null)
            query = query.Where(invitation => invitation.Status == status);

        // Filtro del CRM: las invitaciones de un cliente concreto (portal). Solo casa con las de
        // portal, que son las únicas que llevan CustomerId.
        if (customerId is not null)
            query = query.Where(invitation => invitation.CustomerId == customerId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(invitation => invitation.CreatedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return (items, total);
    }

    private IQueryable<Invitation> Pending(Guid tenantId, string email, UserAccountKind kind) =>
        db
            .Invitations.IgnoreQueryFilters()
            .Where(OfKind(kind))
            .Where(invitation =>
                invitation.TenantId == tenantId
                && invitation.Email == email
                && invitation.Status == InvitationStatus.Pending
                && invitation.ExpiresAtUtc > DateTime.UtcNow
            );

    private static Expression<Func<Invitation, bool>> OfKind(UserAccountKind kind) =>
        kind == UserAccountKind.Portal
            ? invitation => invitation.ActorType == UserActorType.CustomerPortal
            : invitation => invitation.ActorType != UserActorType.CustomerPortal;
}
