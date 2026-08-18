using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Abstractions;

public interface ISignerRoleAuditSnapshotRepository
{
    Task<SignerRoleAuditSnapshot?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    Task AddAsync(SignerRoleAuditSnapshot projection, CancellationToken ct = default);
}
