using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(AuthDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Users.FirstOrDefaultAsync(user => user.Id == id, ct);

    public Task<User?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct = default) =>
        db.Users.FirstOrDefaultAsync(user => user.TenantId == tenantId && user.Email == email, ct);

    public Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken ct = default) =>
        db.Users.AnyAsync(user => user.TenantId == tenantId && user.Email == email, ct);

    public async Task AddAsync(User user, CancellationToken ct = default) => await db.Users.AddAsync(user, ct);

    public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) =>
        db.Users.CountAsync(user => user.TenantId == tenantId && user.IsActive, ct);

    public async Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId,
        int page,
        int size,
        string? search,
        bool? isActive,
        CancellationToken ct = default
    )
    {
        var query = db.Users.AsNoTracking().Where(user => user.TenantId == tenantId);

        if (isActive is not null)
            query = query.Where(user => user.IsActive == isActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(user =>
                user.Name.Contains(term) || user.LastName.Contains(term) || user.Email.Contains(term)
            );
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(user => user.Name)
            .ThenBy(user => user.LastName)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return (items, total);
    }
}
