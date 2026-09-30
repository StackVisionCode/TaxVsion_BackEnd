using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Abstractions;

public interface IInvitationRepository
{
    Task<Invitation?> GetByIdAsync(Guid invitationId, CancellationToken ct = default);
    Task<Invitation?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>¿Hay una invitación pendiente y vigente para ese email y tipo de cuenta?</summary>
    Task<bool> HasPendingAsync(Guid tenantId, string email, UserAccountKind kind, CancellationToken ct = default);

    /// <summary>La invitación pendiente y vigente más reciente para ese email y tipo de cuenta.</summary>
    Task<Invitation?> GetPendingAsync(
        Guid tenantId,
        string email,
        UserAccountKind kind,
        CancellationToken ct = default
    );
    Task AddAsync(Invitation invitation, CancellationToken ct = default);

    /// <summary>Cuenta invitaciones pendientes (no expiradas) que RESERVAN asiento: solo STAFF
    /// (TenantEmployee/TenantAdmin). Las invitaciones de portal (clientes) no cuentan.</summary>
    Task<int> CountPendingAsync(Guid tenantId, CancellationToken ct = default);
    Task<(IReadOnlyList<Invitation> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId,
        InvitationStatus? status,
        int page,
        int size,
        Guid? customerId = null,
        CancellationToken ct = default
    );
}
