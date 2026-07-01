using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(AuthDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Users.FirstOrDefaultAsync(user => user.Id == id, ct);

    public Task<User?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct = default)
        => db.Users.FirstOrDefaultAsync(
            user => user.TenantId == tenantId && user.Email == email,
            ct);

    public Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken ct = default)
        => db.Users.AnyAsync(
            user => user.TenantId == tenantId && user.Email == email,
            ct);

    public async Task AddAsync(User user, CancellationToken ct = default)
        => await db.Users.AddAsync(user, ct);
}
