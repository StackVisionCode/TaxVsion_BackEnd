using BuildingBlocks.Common;
using TaxVision.Auth.Application.Users;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>La cuenta de ese tipo con ese email en la oficina. Staff y Portal pueden compartir email.</summary>
    Task<User?> GetByEmailAsync(Guid tenantId, string email, UserAccountKind kind, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(Guid tenantId, string email, UserAccountKind kind, CancellationToken ct = default);

    /// <summary>El usuario de portal ligado a ese cliente, si existe (activo o no).</summary>
    Task<User?> GetPortalUserByCustomerAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    /// <summary>PayFlow (Fase 16) — idempotencia del endpoint interno de creación de TenantAdmin
    /// por onboarding: un reintento del mismo comando M2M no debe crear un segundo usuario.</summary>
    Task<User?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default);

    /// <summary>
    /// Fase A4 — "encuentra tu oficina": el email es único por tenant, no globalmente,
    /// así que un mismo email puede tener cuentas activas en varios tenants.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(
        string email,
        UserAccountKind kind,
        CancellationToken ct = default
    );
    Task AddAsync(User user, CancellationToken ct = default);

    /// <summary>Cuenta usuarios activos que CONSUMEN asiento: solo STAFF (TenantEmployee/TenantAdmin).
    /// Los usuarios de portal (clientes) no cuentan — no consumen asientos del plan.</summary>
    Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<User>> GetActiveStaffForSlotGatingAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<User>>([]);

    /// <summary>Admin/owner primario del tenant (TenantAdmin activo más antiguo) — destinatario de las
    /// notificaciones de facturación/ciclo de vida de la suscripción. <c>null</c> si no hay ninguno.</summary>
    Task<User?> GetPrimaryAdminAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Cuenta TenantAdmins ACTIVOS del tenant — para no dejarlo sin ningún admin al retirar uno.
    /// Default permisivo para no forzar a los fakes de tests; el repositorio real lo implementa con una query.</summary>
    Task<int> CountActiveAdminsAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(int.MaxValue);

    /// <summary>Usuarios activos que tienen asignado este rol — los titulares a los que hay que
    /// avisarles cuando cambian los permisos del rol. Default vacío para no forzar a los fakes de
    /// tests; el repositorio real lo implementa con una query.</summary>
    Task<IReadOnlyList<User>> GetActiveByRoleAsync(Guid tenantId, Guid roleId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<User>>([]);

    Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId,
        int page,
        int size,
        string? search,
        bool? isActive,
        Guid? customerId = null,
        UserAccountKind? accountKind = null,
        CancellationToken ct = default
    );

    /// <summary>Backfill cross-tenant de owners (TenantAdmin activo) — siembra la proyección local de Notification.
    /// Default vacío para no forzar a los fakes de tests; el repositorio real lo implementa con una query.</summary>
    Task<PagedResult<TenantOwnerReconciliationResponse>> ListTenantOwnersForReconciliationAsync(
        int page,
        int size,
        CancellationToken ct = default
    ) => Task.FromResult(new PagedResult<TenantOwnerReconciliationResponse>([], page, size, 0));

    /// <summary>Backfill cross-tenant de cuentas de portal activas — siembra la proyección local de Notification.
    /// Default vacío para no forzar a los fakes de tests; el repositorio real lo implementa con una query.</summary>
    Task<PagedResult<CustomerPortalAccountReconciliationResponse>> ListPortalAccountsForReconciliationAsync(
        int page,
        int size,
        CancellationToken ct = default
    ) => Task.FromResult(new PagedResult<CustomerPortalAccountReconciliationResponse>([], page, size, 0));

    /// <summary>Backfill cross-tenant del staff (TenantAdmin y TenantEmployee, en cualquier estado) — siembra el
    /// directorio de empleados de Customer. Default vacío para no forzar a los fakes de tests.</summary>
    Task<PagedResult<StaffUserReconciliationResponse>> ListStaffUsersForReconciliationAsync(
        int page,
        int size,
        CancellationToken ct = default
    ) => Task.FromResult(new PagedResult<StaffUserReconciliationResponse>([], page, size, 0));

    /// <summary>Backfill cross-tenant de todos los usuarios (cualquier actor type y estado) con nombre, email y
    /// foto — repara el directorio de Communication. Default vacío para no forzar a los fakes de tests.</summary>
    Task<PagedResult<UserDirectoryReconciliationResponse>> ListUsersForDirectoryReconciliationAsync(
        int page,
        int size,
        CancellationToken ct = default
    ) => Task.FromResult(new PagedResult<UserDirectoryReconciliationResponse>([], page, size, 0));
}
