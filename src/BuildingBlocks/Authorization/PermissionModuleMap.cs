namespace BuildingBlocks.Authorization;

/// <summary>
/// Mapea un código de permiso a su MÓDULO comercial — el mismo <c>module.*</c> que Subscription
/// habilita por plan (ver <c>SubscriptionPlanCatalogSeeder</c>). Fundación del gate de módulo
/// (Entitlements en runtime): el gate consulta este mapa para saber qué módulo exigir por endpoint.
///
/// <para><b>Regla de seguridad:</b> un permiso SIN módulo asignado se considera <b>siempre
/// efectivo</b> — el gate nunca lo bloquea. Así los permisos transversales (auth, billing,
/// subscription, tenant, notification, sms, growth) y cualquier código no mapeado no producen un
/// 403 falso.</para>
///
/// <para><b>Módulos sin permisos backend</b> (<c>marketing</c>, <c>builder</c>, <c>irs</c>,
/// <c>miles</c>) se omiten a propósito: son features de frontend/futuras sin endpoints propios; el
/// frontend ya los muestra/oculta por <c>/auth/me</c>.</para>
///
/// <para><b>Nota de nombres:</b> el módulo es <c>signatures</c> (plural) pero los permisos usan el
/// prefijo <c>signature.</c> (singular); <c>planner</c> agrupa calendar/reminders/tasks/notes;
/// <c>documents</c> agrupa documents/cloudstorage/scribe; <c>email</c> agrupa
/// correspondence/connectors/postmaster/email.</para>
///
/// Función pura, sin estado ni I/O — testeable sin mocks (Single Responsibility).
/// </summary>
public static class PermissionModuleMap
{
    // Prefijo del código de permiso -> módulo. Se evalúa en orden; el primero que calza gana (los
    // prefijos no se solapan entre sí). Mapeo validado con el catálogo de planes 2026-09-14.
    private static readonly (string Prefix, string Module)[] PrefixToModule =
    [
        ("customers.", "customers"),
        ("signature.", "signatures"),
        ("documents.", "documents"),
        ("cloudstorage.", "documents"),
        ("scribe.", "documents"),
        ("calendar.", "planner"),
        ("reminders.", "planner"),
        ("tasks.", "planner"),
        ("notes.", "planner"),
        ("correspondence.", "email"),
        ("connectors.", "email"),
        ("postmaster.", "email"),
        ("email.", "email"),
        ("communication.", "comms"),
        ("comms.", "comms"),
        ("campaigns.", "campaigns"),
        ("reports.", "reports"),
    ];

    /// <summary>El módulo comercial del permiso, o <c>null</c> si no está gateado por módulo (siempre
    /// efectivo).</summary>
    public static string? ModuleFor(string permissionCode)
    {
        if (string.IsNullOrEmpty(permissionCode))
            return null;

        foreach (var (prefix, module) in PrefixToModule)
        {
            if (permissionCode.StartsWith(prefix, StringComparison.Ordinal))
                return module;
        }

        return null;
    }

    /// <summary><c>true</c> si el permiso pertenece a un módulo (y por tanto el gate de módulo debe
    /// verificar que el tenant lo tenga habilitado); <c>false</c> = siempre efectivo.</summary>
    public static bool IsModuleGated(string permissionCode) => ModuleFor(permissionCode) is not null;
}
