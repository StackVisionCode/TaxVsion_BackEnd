using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Application.Accounts;

public interface ITenantEmailAccountRepository
{
    Task AddAsync(TenantEmailAccount account, CancellationToken ct = default);

    /// <summary>
    /// Sin filtro de tenant: los consumidores system-level (background jobs, refresh de tokens)
    /// operan sobre cuentas de todos los tenants por diseño — mismo patrón que los schedulers de
    /// Signature (ExpirationScheduler, ReminderScheduler), que tampoco reciben tenantId.
    /// </summary>
    Task<Result<TenantEmailAccount>> GetByIdAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Sin filtro de tenant, igual que GetByIdAsync — usada por el webhook de Gmail (Fase 7), que
    /// solo recibe el email_address del push notification, nunca el tenant. Una dirección Gmail es
    /// una identidad globalmente única por diseño de Google, así que no hace falta desambiguar.
    /// </summary>
    Task<Result<TenantEmailAccount>> GetByEmailAddressAsync(string emailAddress, CancellationToken ct = default);

    /// <summary>Cuentas del tenant llamante — <c>GET /connectors/accounts</c> (D3 §12.4), a diferencia de GetByIdAsync/GetByEmailAddressAsync sí filtra por tenant porque el caller es un usuario autenticado del frontend.</summary>
    Task<IReadOnlyList<TenantEmailAccount>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Cuentas Active de TODOS los tenants/proveedores — usada por ReconciliationJob, mismo patrón
    /// sin filtro de tenant que GetByIdAsync/GetByEmailAddressAsync (background job system-level).
    /// Solo Active: Draft/Connected todavía no terminaron el setup de watch (SetupWatchHandler), y
    /// Disconnected/Error no deberían sincronizar nada hasta que un reauth manual las reactive.
    /// </summary>
    Task<IReadOnlyList<TenantEmailAccount>> ListActiveAsync(CancellationToken ct = default);
}
