using TaxVision.Auth.Domain.Invitations;

namespace TaxVision.Auth.Application.Abstractions;

public interface IInvitationRepository
{
    Task<Invitation?> GetByIdAsync(Guid invitationId, CancellationToken ct = default);
    Task<Invitation?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task<bool> HasPendingAsync(Guid tenantId, string email, CancellationToken ct = default);
    Task AddAsync(Invitation invitation, CancellationToken ct = default);
}
