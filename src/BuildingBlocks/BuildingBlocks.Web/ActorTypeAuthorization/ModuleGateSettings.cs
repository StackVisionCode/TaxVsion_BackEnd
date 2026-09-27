using BuildingBlocks.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Web.ActorTypeAuthorization;

/// <summary>
/// A6 — decide si el gate de módulo DENIEGA o solo registra, **módulo por módulo**.
///
/// El plan pide un despliegue escalonado (primero <c>campaigns</c>, luego <c>email</c>, luego
/// <c>comms</c> y al final los de Starter) porque encenderlo de golpe es visible para los tenants y
/// un dato malo se paga con 403 en cosas que sí pagaron. El flag original era un booleano de
/// todo-o-nada por servicio, que no permite ese escalón.
///
/// Configuración (`Authorization:ModuleGate`):
/// <code>
/// "ModuleGate": { "Enforce": true, "EnforcedModules": [ "campaigns" ] }
/// </code>
///
/// - `Enforce` ausente o `false` → **nada deniega** (log-only), pase lo que pase en la lista.
/// - `Enforce: true` **con** `EnforcedModules` → denegar SOLO esos módulos; el resto sigue en
///   log-only, así se observa un escalón sin arriesgar los demás.
/// - `Enforce: true` **sin** `EnforcedModules` → denegar todos. Es el estado final, y se deja
///   explícito para no obligar a listar los módulos uno a uno cuando ya se confía en todos.
///
/// Se lee por petición desde <see cref="IConfiguration"/> igual que antes: el coste es una lectura
/// de diccionario en memoria, y a cambio el escalón se sube sin recompilar ni redesplegar código.
/// </summary>
public static class ModuleGateSettings
{
    public const string EnforceKey = "Authorization:ModuleGate:Enforce";
    public const string EnforcedModulesKey = "Authorization:ModuleGate:EnforcedModules";

    /// <summary>¿Este módulo en concreto debe DENEGAR, o solo registrarse?</summary>
    public static bool ShouldEnforce(IConfiguration configuration, string module)
    {
        if (!configuration.GetValue(EnforceKey, false))
            return false;

        var staged = configuration.GetSection(EnforcedModulesKey).Get<string[]>();

        // Lista ausente o vacía = sin escalón: se aplica a todo.
        if (staged is null || staged.Length == 0)
            return true;

        return staged.Contains(module, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Los nombres de <c>EnforcedModules</c> que NO son módulos conocidos. Se comprueba al arrancar
    /// (ver <c>ValidateModuleGateConfiguration</c>): una errata como <c>"campains"</c> deja el módulo
    /// en log-only y quien escribió la configuración cree que lo encendió. Un fallo silencioso hacia
    /// el lado peligroso, que es el que hay que hacer ruidoso.
    /// </summary>
    public static IReadOnlyList<string> UnknownEnforcedModules(IConfiguration configuration)
    {
        var staged = configuration.GetSection(EnforcedModulesKey).Get<string[]>();
        if (staged is null)
            return [];

        return [.. staged.Where(module => !PermissionModuleMap.KnownModules.Contains(module))];
    }

    /// <summary>
    /// Comprobación al arrancar. **Revienta** si el gate está en enforce y la lista nombra un módulo
    /// que no existe: es un error de despliegue, y descubrirlo meses después —porque el módulo que
    /// creías cerrado nunca denegó— es mucho peor que no arrancar. Mismo criterio que el arranque que
    /// ya falla cuando un servicio queda en el modo de permisos equivocado.
    ///
    /// Con el gate apagado solo avisa: ahí la errata no engaña a nadie todavía.
    /// </summary>
    public static void ValidateOrThrow(IConfiguration configuration, ILogger logger)
    {
        var unknown = UnknownEnforcedModules(configuration);
        if (unknown.Count == 0)
            return;

        var names = string.Join(", ", unknown);
        var known = string.Join(", ", PermissionModuleMap.KnownModules.Order(StringComparer.Ordinal));

        if (!configuration.GetValue(EnforceKey, false))
        {
            logger.LogWarning(
                "Module gate: {EnforcedModulesKey} names unknown module(s) [{Unknown}]. Known modules: [{Known}]. "
                    + "The gate is off, so nothing breaks yet — but it would NOT enforce them once turned on.",
                EnforcedModulesKey,
                names,
                known
            );
            return;
        }

        throw new InvalidOperationException(
            $"Module gate is enforcing but {EnforcedModulesKey} names unknown module(s): [{names}]. "
                + $"Known modules: [{known}]. A misspelled name silently leaves that module in log-only."
        );
    }
}
