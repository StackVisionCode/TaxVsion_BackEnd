using TaxVision.Correspondence.Domain.Projections;

namespace TaxVision.Correspondence.Application.Abstractions;

public interface IMailboxOwnershipRepository
{
    Task<MailboxOwnership?> GetAsync(Guid tenantId, Guid accountId, CancellationToken ct = default);

    Task AddAsync(MailboxOwnership ownership, CancellationToken ct = default);

    /// <summary>Los buzones del usuario en cualquier estado, o <c>null</c> si el tenant aún no tiene proyección.</summary>
    Task<IReadOnlyCollection<Guid>?> ListOwnedAccountIdsAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    );
}
