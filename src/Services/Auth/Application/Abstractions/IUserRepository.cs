using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken ct = default);

    /// <summary>
    /// Fase A4 — "encuentra tu oficina": el email es único por tenant, no globalmente,
    /// así que un mismo email puede tener cuentas activas en varios tenants.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(string email, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default);
    Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId,
        int page,
        int size,
        string? search,
        bool? isActive,
        CancellationToken ct = default
    );
}
