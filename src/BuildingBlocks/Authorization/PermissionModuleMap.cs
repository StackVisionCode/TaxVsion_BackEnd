namespace BuildingBlocks.Authorization;

/// <summary>
/// Mapea un código de permiso a su MÓDULO comercial — el mismo <c>module.*</c> que Subscription
/// habilita por plan (ver <c>SubscriptionPlanCatalogSeeder</c>). Fundación del gate de módulo
/// (Entitlements en runtime): el gate consulta este mapa para saber qué módulo exigir por endpoint.
///
/// <para><b>Regla de seguridad:</b> un permiso SIN módulo asignado se considera <b>siempre
/// efectivo</b> — el gate nunca lo bloquea. Así los permisos transversales (auth, billing,
/// subscription, tenant, notification, sms, growth) y cualquier código no mapeado no producen un
/// 403 falso. La lista <see cref="Exempt"/> mete en esa misma categoría a los pocos códigos que un
/// prefijo llevaría a un módulo sin ser la feature que ese módulo vende.</para>
///
/// <para><b>Decisión arquitectónica (CAT-F6 #234 revisado 2026-10-16, mantener):</b> este mapa
/// parece duplicar <c>PermissionDefinition.GatedModule</c> (persistido en la DB de Auth y seedeado
/// desde <see cref="ModuleFor"/>). <b>No lo es.</b> Esta tabla es la fuente CROSS-SERVICE: la
/// consulta <c>BuildingBlocks.Web.ActorTypeAuthorization.PermissionPolicyProvider</c> en CADA
/// request HTTP de los 25 microservicios para decidir "¿este permiso requiere módulo?" sin tocar la
/// DB de Auth ni hacer M2M. <c>PermissionDefinition.GatedModule</c> es el snapshot persistido que
/// vive SÓLO en Auth (usado en queries admin). Communication-TS tiene su propio espejo del mapa por
/// la misma razón. Retirar <see cref="PrefixToModule"/> requeriría introducir un canal de
/// proyección cross-service (evento + consumer + tabla en los 25 servicios) para una tabla de ~18
/// entradas que cambia 1 vez cada 6 meses — mismo trade-off que <see cref="KnownModules"/>, que
/// también se mantiene (CAT-F9-G4-B.2/B.3).</para>
///
/// <para><b>Módulos sin permisos backend</b> (<c>marketing</c>, <c>builder</c>, <c>irs</c>,
/// <c>miles</c>) se omiten a propósito: son features de frontend/futuras sin endpoints propios; el
/// frontend ya los muestra/oculta por <c>/auth/me</c>.</para>
///
/// <para><b>Nota de nombres:</b> el módulo es <c>signatures</c> (plural) pero los permisos usan el
/// prefijo <c>signature.</c> (singular); <c>planner</c> agrupa calendar/reminders/tasks/notes;
/// <c>documents</c> agrupa documents/cloudstorage/scribe; <c>email</c> agrupa
/// correspondence/connectors/postmaster/email; <c>comms</c> es chat, llamadas y vídeo, y
/// <c>meetings</c> se separa de él porque se vende aparte.</para>
///
/// Función pura, sin estado ni I/O — testeable sin mocks (Single Responsibility).
/// </summary>
public static class PermissionModuleMap
{
    // Prefijo del código de permiso -> módulo. Se evalúa en orden y **el primero que calza gana**.
    //
    // ⚠️ `communication.meeting.` y `communication.` SÍ se solapan, y es el único par que lo hace: las
    // reuniones se venden aparte del chat (Pro/Enterprise) mientras que chat, llamadas y vídeo van en
    // todos los planes. Por eso el prefijo más específico tiene que ir ANTES; invertirlos deja las
    // reuniones dentro de `comms` en silencio y las regala en Starter. Lo fija un test.
    //
    // Mapeo validado con el catálogo de planes 2026-09-14.
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
        ("communication.meeting.", "meetings"),
        ("communication.", "comms"),
        ("comms.", "comms"),
        ("campaigns.", "campaigns"),
        ("wallet.", "wallet"),
    ];

    /// <summary>
    /// A6/A5.2 — permisos que caen bajo un prefijo con módulo pero que el gate NO debe exigir.
    ///
    /// No son una excepción cosmética: los tres viven en el microservicio Communication —y por eso el
    /// prefijo los llevaría a <c>comms</c>— pero ninguno ES la feature que se vende con ese módulo.
    /// <c>SystemRoleDefaults</c> los otorga a CUALQUIER tenant, así que con el gate en enforce un
    /// Starter los tendría en su token y recibiría 403:
    ///
    /// <list type="bullet">
    /// <item><c>notification.read</c>: las notificaciones in-app son transversales — avisan de
    /// documentos, firmas y tareas, que el tenant sí paga. Gatearlas por <c>comms</c> apagaría la
    /// campanita de TODO lo demás (riesgo "Alta/Alto" del plan).</item>
    /// <item><c>support.open</c>: abrir soporte hacia la plataforma. Es el camino para SALIR de un
    /// problema de plan o de pago; exigir el módulo dejaría sin voz justo al tenant que peor está.</item>
    /// <item><c>support.agent</c>: el otro lado del mismo chat, que atiende el tenant de la
    /// plataforma. No hay plan que lo habilite porque no se vende.</item>
    /// </list>
    ///
    /// Se resuelven acá y no en el gate para que las TRES lecturas del mapa coincidan sin coordinarse:
    /// el gate en runtime, el techo de plan al otorgar permisos y el bootstrap
    /// <c>/auth/me/effective-access</c> que leen los frontends. Un `null` significa lo mismo en los
    /// tres sitios: nada lo gatea.
    /// </summary>
    private static readonly IReadOnlySet<string> ExemptPermissions = new HashSet<string>(StringComparer.Ordinal)
    {
        CommunicationPermissions.NotificationRead,
        CommunicationPermissions.SupportOpen,
        CommunicationPermissions.SupportAgent,
    };

    /// <summary>Los permisos exentos del gate, para tests y para documentar la decisión.</summary>
    public static IReadOnlySet<string> Exempt => ExemptPermissions;

    /// <summary>El módulo comercial del permiso, o <c>null</c> si no está gateado por módulo (siempre
    /// efectivo).</summary>
    public static string? ModuleFor(string permissionCode)
    {
        if (string.IsNullOrEmpty(permissionCode))
            return null;

        if (ExemptPermissions.Contains(permissionCode))
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

    /// <summary>
    /// Los módulos que este mapa puede llegar a exigir. Es la lista contra la que se valida cualquier
    /// otro sitio que nombre un módulo — el escalón del gate (<c>EnforcedModules</c>) y el catálogo
    /// de planes.
    ///
    /// Existe porque un nombre mal escrito falla EN SILENCIO y hacia el lado peligroso: un
    /// <c>"campains"</c> en la configuración deja el módulo en log-only creyendo que se encendió, y
    /// un módulo del catálogo que este mapa no conoce no gatea nada aunque el plan lo cobre.
    /// </summary>
    public static IReadOnlySet<string> KnownModules { get; } =
        PrefixToModule.Select(entry => entry.Module).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
