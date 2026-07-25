using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(AuthDbContext db) : IUserRepository
{
    // IgnoreQueryFilters() explícito: este repo se invoca desde handlers de Wolverine
    // (bus.InvokeAsync), en un scope de DI distinto al de la request HTTP que pobló
    // ITenantContext vía JwtTenantContextMiddleware — el HasQueryFilter ambiental de
    // AuthDbContext ve Guid.Empty ahí, así que esta consulta por Id puro siempre devolvía 0
    // filas (ver /auth/me → 404). Es seguro: todos los llamadores de GetByIdAsync o bien pasan
    // el propio Id del actor autenticado (self, ej. GetMe/UpdateMyProfile/comandos de MFA), o
    // bien hacen la validación de tenant explícita post-fetch (target.TenantId != command.TenantId,
    // ej. Deactivate/Reactivate/AssignUserRoles/GetUserById/GetUserSessions) — el filtro ambiental
    // era redundante con esa guarda, nunca la única barrera de aislamiento.
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(user => user.Id == id, ct);

    // IgnoreQueryFilters() explícito en las 3 queries de abajo: el filtro global fail-closed de
    // RBAC Fase 5 compara contra ITenantContext (poblado por JwtTenantContextMiddleware desde el
    // JWT) — pero Login/ForgotPassword/RequestTenantRecovery corren ANTES de tener un JWT
    // (AllowAnonymous), así que ese contexto siempre está vacío ahí y el filtro bloqueaba
    // silenciosamente estas 3 consultas (siempre 0 filas), rompiendo login y forgot-password para
    // cualquier tenant. El tenantId de cada Where() ya viene validado por otra vía en cada
    // llamador (Host resolution en Login, el propio invitation token en otros flujos) — mismo
    // criterio que ya usan los background jobs de Fase 5 (ver AuthMaintenanceService).
    public Task<User?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct = default) =>
        db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(user => user.TenantId == tenantId && user.Email == email, ct);

    public Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken ct = default) =>
        db.Users.IgnoreQueryFilters().AnyAsync(user => user.TenantId == tenantId && user.Email == email, ct);

    public async Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(
        string email,
        CancellationToken ct = default
    ) =>
        await db
            .Users.IgnoreQueryFilters()
            .Where(user => user.Email == email && user.IsActive)
            .Select(user => user.TenantId)
            .Distinct()
            .ToListAsync(ct);

    public async Task AddAsync(User user, CancellationToken ct = default) => await db.Users.AddAsync(user, ct);

    public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) =>
        db.Users.IgnoreQueryFilters().CountAsync(user => user.TenantId == tenantId && user.IsActive, ct);

    public async Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId,
        int page,
        int size,
        string? search,
        bool? isActive,
        CancellationToken ct = default
    )
    {
        var query = db.Users.IgnoreQueryFilters().AsNoTracking().Where(user => user.TenantId == tenantId);

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
