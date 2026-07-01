using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
}
