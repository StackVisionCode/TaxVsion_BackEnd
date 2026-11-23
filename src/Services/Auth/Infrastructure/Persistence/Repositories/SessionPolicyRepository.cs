using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Sessions;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

public sealed class SessionPolicyRepository(AuthDbContext db) : ISessionPolicyRepository
{
    public Task<PlatformSessionPolicy?> GetAsync(CancellationToken ct = default) =>
        db.PlatformSessionPolicies.FirstOrDefaultAsync(policy => policy.Id == PlatformSessionPolicy.GlobalId, ct);

    public async Task AddAsync(PlatformSessionPolicy policy, CancellationToken ct = default) =>
        await db.PlatformSessionPolicies.AddAsync(policy, ct);
}
