using TaxVision.Postmaster.Domain.Projections;

namespace TaxVision.Postmaster.Application.Abstractions;

public interface ITenantOAuthAccountRepository
{
    Task<TenantOAuthAccount?> GetByAccountIdAsync(Guid tenantId, Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// La cuenta activa más recientemente conectada para el tenant. Regla interina explícita: si un
    /// tenant llega a conectar más de una cuenta, la última conexión gana — no existe hoy un concepto
    /// de "cuenta primaria" seleccionable por el usuario.
    /// </summary>
    Task<TenantOAuthAccount?> FindActiveByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(TenantOAuthAccount account, CancellationToken ct = default);
}
